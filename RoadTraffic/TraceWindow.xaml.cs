using System;
using System.Windows;

namespace RoadTraffic
{
    public partial class TraceWindow : Window
    {
        private const int MaxTraceCharacters = 40000;

        public TraceWindow()
        {
            InitializeComponent();
            RuntimeDiagnostics.LineWritten += OnTraceLineWritten;
        }

        protected override void OnClosed(EventArgs e)
        {
            RuntimeDiagnostics.LineWritten -= OnTraceLineWritten;
            base.OnClosed(e);
        }

        private void OnTraceLineWritten(string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string>(OnTraceLineWritten), line);
                return;
            }

            TraceTextBox.AppendText(line + Environment.NewLine);
            if (TraceTextBox.Text.Length > MaxTraceCharacters)
                TraceTextBox.Text = TraceTextBox.Text.Substring(TraceTextBox.Text.Length - MaxTraceCharacters);

            TraceTextBox.ScrollToEnd();
        }
    }
}
