﻿namespace tomenglertde.ResXManager.View.Tools
{
    using System;
    using System.ComponentModel;
    using System.ComponentModel.Composition;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Windows.Threading;

    using JetBrains.Annotations;

    using tomenglertde.ResXManager.Infrastructure;
    using tomenglertde.ResXManager.Model;

    using TomsToolbox.Wpf;

    [Export]
    public class PerformanceTracer
    {
        [NotNull]
        private readonly ITracer _tracer;
        [NotNull]
        private readonly Configuration _configuration;
        private int _index;

        [ImportingConstructor]
        public PerformanceTracer([NotNull] ITracer tracer, [NotNull] Configuration configuration)
        {
            _tracer = tracer;
            _configuration = configuration;
        }

        [CanBeNull]
        public IDisposable Start([Localizable(false)][NotNull] string message)
        {
            if (!_configuration.ShowPerformanceTraces)
                return null;

            return new Tracer(_tracer, Interlocked.Increment(ref _index), message);
        }

        public void Start([Localizable(false)][NotNull] string message, DispatcherPriority priority)
        {
            var tracer = Start(message);
            if (tracer == null)
                return;

            Dispatcher.CurrentDispatcher.BeginInvoke(priority, () => tracer.Dispose());
        }

        private sealed class Tracer : IDisposable
        {
            [NotNull]
            private readonly ITracer _tracer;
            private readonly int _index;
            [NotNull]
            private readonly string _message;
            [NotNull]
            private readonly Stopwatch _stopwatch = new Stopwatch();

            public Tracer([NotNull] ITracer tracer, int index, [NotNull] string message)
            {
                _tracer = tracer;
                _index = index;
                _message = message;

                _stopwatch.Start();

                _tracer.WriteLine(">>> {0}: {1} @{2}", _index, _message, DateTime.Now.ToString("HH:mm:ss.f", CultureInfo.InvariantCulture));
            }


            public void Dispose()
            {
                _tracer.WriteLine("<<< {0}: {1} {2}", _index, _message, _stopwatch.Elapsed);

                _stopwatch.Stop();
            }
        }
    }
}
