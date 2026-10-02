using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TalkPro.Infrastructure.Storage;

namespace TalkPro.App;

public partial class App : Application
{
    private IHost? _host;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppPaths.CreateDefault();
        paths.EnsureCreated();

        _host = AppHost.Build(paths);
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        RegisterGlobalExceptionHandlers();

        _host.Start();
        AppLog.Started(_logger);
        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    // Exception messages may contain user content, so only the content-free logger sees the
    // exception and the user gets a generic notice.
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.UnhandledUiException(_logger, e.Exception);
        }

        MessageBox.Show(
            "예기치 않은 오류가 발생했습니다. 오류 정보는 대화 내용 없이 이 PC에만 기록됩니다.",
            "TalkPro",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (_logger is not null && e.ExceptionObject is Exception exception)
        {
            AppLog.UnhandledDomainException(_logger, exception, e.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.UnobservedTaskException(_logger, e.Exception);
        }

        e.SetObserved();
    }
}

internal static partial class AppLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Application started")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Unhandled UI exception")]
    public static partial void UnhandledUiException(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Critical, Message = "Unhandled domain exception (terminating: {IsTerminating})")]
    public static partial void UnhandledDomainException(ILogger logger, Exception exception, bool isTerminating);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Unobserved task exception")]
    public static partial void UnobservedTaskException(ILogger logger, Exception exception);
}
