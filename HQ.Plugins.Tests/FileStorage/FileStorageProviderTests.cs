using HQ.Models.Interfaces;
using HQ.Plugins.FileStorage;

namespace HQ.Plugins.Tests.FileStorage;

public class FileStorageProviderTests
{
    [Fact]
    public void FileStorageCommand_ImplementsIFileStorageProvider()
    {
        var command = new FileStorageCommand();
        Assert.IsAssignableFrom<IFileStorageProvider>(command);
    }

    [Fact]
    public void FileStorageCommand_ImplementsICommand()
    {
        var command = new FileStorageCommand();
        Assert.IsAssignableFrom<ICommand>(command);
    }

    [Fact]
    public void SetFileStorageProvider_DoesNotThrow()
    {
        // FileStorageCommand doesn't need a provider injected (it IS the provider),
        // but SetFileStorageProvider should still work as a no-op
        var command = new FileStorageCommand();
        var asCommand = (ICommand)command;
        asCommand.SetFileStorageProvider(command);
    }
}
