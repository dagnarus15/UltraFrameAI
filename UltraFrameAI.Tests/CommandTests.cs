using UltraFrameAI;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class CommandTests
{
    [Fact]
    public void RelayCommand_Executes_AndRaisesCanExecuteChanged()
    {
        var executed = false;
        var canExecute = true;
        var command = new RelayCommand(
            () => executed = true,
            () => canExecute);

        var canExecuteChanged = 0;
        command.CanExecuteChanged += (_, _) => canExecuteChanged++;

        Assert.True(command.CanExecute(null));
        command.Execute(null);
        Assert.True(executed);

        canExecute = false;
        command.RaiseCanExecuteChanged();

        Assert.False(command.CanExecute(null));
        Assert.Equal(1, canExecuteChanged);
    }

    [Fact]
    public async Task AsyncRelayCommand_DisablesWhileRunning()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(async () =>
        {
            started.SetResult();
            await release.Task;
        });

        var canExecuteChanged = 0;
        command.CanExecuteChanged += (_, _) => canExecuteChanged++;

        Assert.True(command.CanExecute(null));
        command.Execute(null);

        await started.Task;
        Assert.False(command.CanExecute(null));

        release.SetResult();
        await Task.Delay(50);

        Assert.True(command.CanExecute(null));
        Assert.True(canExecuteChanged >= 2);
    }
}
