using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using UltraFrameAI;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class MainViewModelThreadingTests
{
    [Fact]
    public async Task QueueItem_IsBusy_ChangedFromBackgroundThread_UpdatesCanExecuteWithoutThrowing()
    {
        await RunOnStaThreadAsync(() =>
        {
            if (Application.Current is null)
            {
                _ = new Application();
            }

            var viewModel = new MainViewModel(persistUserState: false);

            var item = new QueueItemViewModel
            {
                Title = "episode.mkv",
                SourcePath = "source.mkv",
                OutputPath = "output.mkv"
            };

            viewModel.Items.Add(item);
            AttachQueueItem(viewModel, item);
            viewModel.NotifyQueueSelectionChanged();
            Assert.True(viewModel.CanStartAll);

            var background = Task.Run(() => item.IsBusy = true);
            background.GetAwaiter().GetResult();
            WaitForCondition(() => item.IsBusy);
            viewModel.UpdateActionStates();

            Assert.True(item.IsBusy);
        });
    }

    private static void AttachQueueItem(MainViewModel viewModel, QueueItemViewModel item)
    {
        var method = typeof(MainViewModel).GetMethod("AttachQueueItem", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new MissingMethodException(nameof(MainViewModel), "AttachQueueItem");
        }

        method.Invoke(viewModel, new object[] { item });
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void WaitForCondition(Func<bool> condition, int attempts = 20)
    {
        for (var i = 0; i < attempts; i++)
        {
            PumpDispatcher();
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }
    }

    private static Task RunOnStaThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
