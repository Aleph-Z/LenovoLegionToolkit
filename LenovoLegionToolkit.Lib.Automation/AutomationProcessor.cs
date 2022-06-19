﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Utils;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.Automation
{
    public class AutomationProcessor
    {
        private readonly AutomationSettings _settings;
        private readonly PowerStateListener _powerStateListener;
        private readonly ProcessListener _processListener;

        private readonly AsyncLock _lock = new();

        private List<AutomationPipeline> _pipelines = new();
        private CancellationTokenSource? _cts;

        public bool IsEnabled
        {
            get => _settings.IsEnabled;
            set
            {
                _settings.IsEnabled = value;
                _settings.Synchronize();
            }
        }

        public event EventHandler<List<AutomationPipeline>>? PipelinesChanged;

        public AutomationProcessor(AutomationSettings settings, PowerStateListener powerStateListener, ProcessListener processListener)
        {
            _settings = settings;
            _powerStateListener = powerStateListener;
            _processListener = processListener;

            _powerStateListener.Changed += PowerStateListener_Changed;
            _processListener.Changed += ProcessListener_Changed;
        }

        private async void ProcessListener_Changed(object? sender, ProcessEventInfo e)
        {
            await RunAsync(e);
        }

        private async void PowerStateListener_Changed(object? sender, EventArgs e)
        {
            await RunAsync();
        }

        public async Task InitializeAsync()
        {
            using (await _lock.LockAsync().ConfigureAwait(false))
            {
                _pipelines = _settings.Pipeliness;

                PipelinesChanged?.Invoke(this, _pipelines.Select(p => p.DeepCopy()).ToList());
            }
        }

        public async Task ReloadPipelinesAsync(List<AutomationPipeline> pipelines)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Pipelines reload pending...");

            using (await _lock.LockAsync().ConfigureAwait(false))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Pipelines reloading...");

                _pipelines = pipelines.Select(p => p.DeepCopy()).ToList();

                _settings.Pipeliness = pipelines;
                _settings.Synchronize();

                PipelinesChanged?.Invoke(this, _pipelines.Select(p => p.DeepCopy()).ToList());

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Pipelines reloaded.");
            }
        }

        public async Task<List<AutomationPipeline>> GetPipelinesAsync()
        {
            using (await _lock.LockAsync().ConfigureAwait(false))
                return _pipelines.Select(p => p.DeepCopy()).ToList();
        }

        public void RunOnStartup()
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Pipeline run on startup pending...");

            _ = Task.Run(RunAsync);
        }

        public async Task RunNowAsync(AutomationPipeline pipeline)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Pipeline run now pending...");

            using (await _lock.LockAsync().ConfigureAwait(false))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Pipeline run starting...");

                try
                {
                    await pipeline.DeepCopy().RunAsync().ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Pipeline run finished successfully.");
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Pipeline run failed: {ex.Demystify()}");

                    throw;
                }
            }
        }

        private Task RunAsync() => RunAsync(null);

        private async Task RunAsync(object? context)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Run pending...");

            using (await _lock.LockAsync().ConfigureAwait(false))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Run starting...");

                _cts?.Cancel();

                if (!IsEnabled)
                    return;

                _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                foreach (var pipeline in _pipelines)
                {
                    if (ct.IsCancellationRequested)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Run interrupted.");
                        break;
                    }

                    try
                    {
                        if (pipeline.Trigger is null || !await pipeline.Trigger.IsSatisfiedAsync(context))
                        {
                            if (Log.Instance.IsTraceEnabled)
                                Log.Instance.Trace($"Pipeline triggers not satisfied. [name={pipeline.Name}, trigger={pipeline.Trigger}, steps.Count={pipeline.Steps.Count}]");
                            continue;
                        }

                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Running pipeline... [name={pipeline.Name}, trigger={pipeline.Trigger}, steps.Count={pipeline.Steps.Count}]");

                        await pipeline.RunAsync(token: ct).ConfigureAwait(false);

                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Pipeline completed successfully. [name={pipeline.Name}, trigger={pipeline.Trigger}]");
                    }
                    catch (Exception ex)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Pipeline run failed: {ex.Demystify()} [name={pipeline.Name}, trigger={pipeline.Trigger}]");
                    }

                    if (pipeline.IsExclusive)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Pipeline is exclusive. Breaking. [name={pipeline.Name}, trigger={pipeline.Trigger}, steps.Count={pipeline.Steps.Count}]");
                        break;
                    }
                }

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Run finished successfully.");
            }
        }
    }
}