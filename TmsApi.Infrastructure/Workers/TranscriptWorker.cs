using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TmsApi.Application.Transcripts;
using TmsApi.Infrastructure.Transcripts;

namespace TmsApi.Infrastructure.Workers;

public class TranscriptWorker : BackgroundService
{
    private readonly Channel<TranscriptRequest> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITranscriptStatusStore _statusStore;
    private readonly ILogger<TranscriptWorker> _logger;

    public TranscriptWorker(
        Channel<TranscriptRequest> channel,
        IServiceScopeFactory scopeFactory,
        ITranscriptStatusStore statusStore,
        ILogger<TranscriptWorker> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _statusStore = statusStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Transcript worker started.");

        await foreach (var request in _channel.Reader.ReadAllAsync(ct))
        {
            var reportId = request.ReportId
                ?? throw new InvalidOperationException("ReportId must be set before queueing.");

            try
            {
                await _statusStore.MarkProcessingAsync(reportId, ct);
                _logger.LogInformation("Generating transcript {ReportId} for student {StudentId}", reportId, request.StudentId);

                await Task.Delay(TimeSpan.FromSeconds(5), ct);

                var downloadUrl = $"/api/v2/transcripts/{reportId}/download";
                await _statusStore.MarkReadyAsync(reportId, downloadUrl, ct);

                _logger.LogInformation("Transcript ready: {ReportId}", reportId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Worker shutdown - transcript {ReportId} did not complete", reportId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate transcript {ReportId}", reportId);
                await _statusStore.MarkFailedAsync(reportId, ex.Message, CancellationToken.None);
            }
        }
    }
}
