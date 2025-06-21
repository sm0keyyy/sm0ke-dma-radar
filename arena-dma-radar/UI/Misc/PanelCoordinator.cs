using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace arena_dma_radar.UI.Misc
{
    public class PanelCoordinator
    {
        private static PanelCoordinator _instance;
        public static PanelCoordinator Instance => _instance ??= new PanelCoordinator();

        private readonly Dictionary<string, bool> _panelReadyState = new();
        private readonly HashSet<string> _requiredPanels = new();
        private bool _allPanelsReady = false;
        private TaskCompletionSource<bool> _allPanelsReadyTcs = new TaskCompletionSource<bool>();

        public event EventHandler AllPanelsReady;

        public void RegisterRequiredPanel(string panelName)
        {
            _requiredPanels.Add(panelName);
            _panelReadyState[panelName] = false;
        }

        public void SetPanelReady(string panelName)
        {
            if (_panelReadyState.ContainsKey(panelName))
            {
                _panelReadyState[panelName] = true;
                CheckAllPanelsReady();
            }
        }

        private void CheckAllPanelsReady()
        {
            if (_allPanelsReady) return;

            if (_requiredPanels.All(panel => _panelReadyState.ContainsKey(panel) && _panelReadyState[panel]))
            {
                _allPanelsReady = true;
                AllPanelsReady?.Invoke(this, EventArgs.Empty);
                _allPanelsReadyTcs.TrySetResult(true);
            }
        }

        public bool IsPanelReady(string panelName)
        {
            return _panelReadyState.ContainsKey(panelName) && _panelReadyState[panelName];
        }

        public async Task WaitForAllPanelsAsync(int timeoutMs = 10000)
        {
            if (_allPanelsReady) return;

            using var cts = new CancellationTokenSource(timeoutMs);
            var timeoutTask = Task.Delay(timeoutMs, cts.Token);
            var completedTask = await Task.WhenAny(_allPanelsReadyTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"Timed out waiting for all panels to be ready after {timeoutMs}ms");
            }

            cts.Cancel();
        }

        public void Reset()
        {
            foreach (var panel in _panelReadyState.Keys.ToList())
            {
                _panelReadyState[panel] = false;
            }
            _allPanelsReady = false;
            _allPanelsReadyTcs = new TaskCompletionSource<bool>();
        }
    }
}
