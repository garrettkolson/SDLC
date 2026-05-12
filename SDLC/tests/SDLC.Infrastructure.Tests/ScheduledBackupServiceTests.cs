using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SDLC.Infrastructure.Backup;

namespace SDLC.Infrastructure.Tests;

[TestFixture]
public class ScheduledBackupServiceTests
{
    private SQLiteBackupService _backupService = null!;
    private BackupConfig _config = null!;
    private InMemoryLogger<ScheduledBackupService> _logger = null!;
    private ILogger<SQLiteBackupService> _backupLogger = null!;
    private ScheduledBackupService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _config = new BackupConfig
        {
            BackupsDirectory = "/backups",
            DatabaseFile = "/sdlc.db",
            ArtifactsDirectory = "/artifacts",
            RetentionDays = 30,
            EnableAutoCleanup = true
        };
        var fileManager = Substitute.For<IFileManager>();
        _backupLogger = Substitute.For<ILogger<SQLiteBackupService>>();
        _backupService = new SQLiteBackupService(fileManager, _config, _backupLogger);
        _logger = new InMemoryLogger<ScheduledBackupService>();
        _service = new ScheduledBackupService(_backupService, Options.Create(_config), _logger);
    }

    [TearDown]
    public void TearDown()
    {
        (_service as IDisposable)?.Dispose();
    }

    [Test]
    public void StartAsync_CanBeCalledWithoutError()
    {
        var ct = CancellationToken.None;
        var task = _service.StartAsync(ct);
        task.IsCompleted.Should().BeTrue();
    }

    [Test]
    public async Task StartAsync_DoesNotThrow()
    {
        var ct = CancellationToken.None;
        var act = () => _service.StartAsync(ct);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ExecuteAsync_LogsStartMessage()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        _ = Task.Run(async () => await _service.StartAsync(cts.Token));
        await Task.Delay(250);

        _logger.Entries.Should().NotBeEmpty();
        _logger.Entries[0].Message.Should().Contain("Scheduled backup service starting");
    }

    [Test]
    public async Task ExecuteAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        _ = Task.Run(async () => await _service.StartAsync(cts.Token));
        await Task.Delay(200);

        // Service should stop cleanly
    }

    [Test]
    public void StopAsync_ReturnsCompletedTask()
    {
        var ct = CancellationToken.None;
        _service.StopAsync(ct).IsCompleted.Should().BeTrue();
    }

    private class InMemoryLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }

        public record LogEntry(LogLevel LogLevel, EventId EventId, string Message, Exception? Exception);
    }
}
