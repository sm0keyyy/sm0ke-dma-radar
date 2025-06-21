using eft_dma_shared.Common.Misc;
using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.Arena.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;

namespace arena_dma_radar.Arena.Features.MemoryWrites
{
    public sealed class MoveSpeed : MemWriteFeature<MoveSpeed>
    {
        private const float BASE_SPEED = 1.0f;
        private float _lastSpeed;
        private bool _lastEnabledState;
        private ulong _cachedAnimator;

        public override bool Enabled
        {
            get => MemWrites.Config.MoveSpeed.Enabled;
            set => MemWrites.Config.MoveSpeed.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var configSpeed = MemWrites.Config.MoveSpeed.Multiplier;
                var stateChanged = Enabled != _lastEnabledState;
                var speedChanged = _lastSpeed != configSpeed;

                if ((Enabled && (stateChanged || speedChanged)) || (!Enabled && stateChanged))
                {
                    var animator = GetAnimator(localPlayer);
                    if (!animator.IsValidVirtualAddress())
                        return;

                    var targetSpeed = Enabled ? configSpeed : BASE_SPEED;
                    var current = Memory.ReadValue<float>(animator + UnityOffsets.UnityAnimator.Speed, false);

                    ValidateSpeed(current, configSpeed);

                    writes.AddValueEntry(animator + UnityOffsets.UnityAnimator.Speed, targetSpeed);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        _lastSpeed = configSpeed;
                        LoneLogging.WriteLine($"[MoveSpeed] {(Enabled ? $"Enabled (Speed: {targetSpeed:F2})" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[MoveSpeed]: {ex}");
                _cachedAnimator = default;
            }
        }

        private ulong GetAnimator(LocalPlayer localPlayer)
        {
            if (_cachedAnimator.IsValidVirtualAddress())
                return _cachedAnimator;

            var pAnimators = Memory.ReadPtr(localPlayer + Offsets.Player._animators);
            if (!pAnimators.IsValidVirtualAddress())
                return 0x0;

            using var animators = MemArray<ulong>.Get(pAnimators);
            if (animators.Count == 0)
                return 0x0;

            var animator = Memory.ReadPtrChain(animators[0], new uint[] {
                Offsets.BodyAnimator.UnityAnimator,
                ObjectClass.MonoBehaviourOffset
            });

            if (!animator.IsValidVirtualAddress())
                return 0x0;

            _cachedAnimator = animator;
            return animator;
        }

        private static void ValidateSpeed(float currentSpeed, float configSpeed)
        {
            if (!float.IsNormal(currentSpeed) ||
                currentSpeed < BASE_SPEED - 0.2f ||
                currentSpeed > configSpeed + 0.2f)
            {
                throw new ArgumentOutOfRangeException(nameof(currentSpeed),
                    $"Current speed {currentSpeed} is out of valid range [{BASE_SPEED - 0.2f:F1}, {configSpeed + 0.2f:F1}]");
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _lastSpeed = default;
            _cachedAnimator = default;
        }
    }
}