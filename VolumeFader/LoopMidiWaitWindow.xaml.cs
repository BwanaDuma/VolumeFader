using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VolumeFader
{
    public partial class LoopMidiWaitWindow : Window
    {
        private CancellationTokenSource? _cts;
        public bool WaitCanceled { get; private set; } = false;

        public LoopMidiWaitWindow(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
            OkButton.Visibility = Visibility.Collapsed; // hide OK, only allow Cancel
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            WaitCanceled = true;
            try { _cts?.Cancel(); } catch { }
            this.Close();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        public async Task<bool> WaitFor(Func<bool> checkFunc, TimeSpan retryInterval, TimeSpan timeout)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (!token.IsCancellationRequested && sw.Elapsed < timeout)
            {
                try
                {
                    if (checkFunc())
                        return true;
                }
                catch { }

                try
                {
                    await Task.Delay(retryInterval, token);
                }
                catch (OperationCanceledException) { break; }
            }

            return false;
        }
    }
}
