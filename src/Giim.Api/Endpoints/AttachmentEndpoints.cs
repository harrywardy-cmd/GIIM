using Giim.Api.Security;
using Giim.Domain.Assets;
using Giim.Infrastructure.Attachments;
using FromForm = Microsoft.AspNetCore.Mvc.FromFormAttribute;
using Microsoft.Net.Http.Headers;

namespace Giim.Api.Endpoints;

internal sealed record RemoveAttachmentRequest(string? Reason, string? TicketNumber);

/// <summary>Files kept with an asset: photos, invoices, warranty documents, disposal certificates.</summary>
internal static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder api)
    {
        // Antiforgery tokens aren't used: every change must carry the X-GIIM-Request header instead (AuthSetup.UseCsrfHeaderCheck).
        var group = api.MapGroup("/api/assets/{assetId:guid}/attachments").DisableAntiforgery();

        group.MapGet("/", async (Guid assetId, AttachmentService files, CancellationToken ct) =>
            Results.Ok(await files.ListAsync(assetId, ct)));

        group.MapPost("/", (Guid assetId, IFormFile file, [FromForm] AttachmentKind? kind, [FromForm] string? description,
            [FromForm] string? ticketNumber, AttachmentService files, ICurrentUser user, CancellationToken ct) => AssetEndpoints.Handle(async () =>
        {
            await using var content = file.OpenReadStream();
            var added = await files.AddAsync(assetId, new AttachmentUpload(file.FileName, content, kind ?? AttachmentKind.Other, description),
                new ActionContext(user.Name, ticketNumber), ct);
            return Results.Ok(new { added.Id, added.FileName });
        }));

        group.MapGet("/{attachmentId:guid}/content", (Guid assetId, Guid attachmentId, bool? download, AttachmentService files,
            HttpContext http, CancellationToken ct) => AssetEndpoints.Handle(async () =>
        {
            var (file, content) = await files.OpenAsync(assetId, attachmentId, ct);

            // Photos are shown in the page; everything else is downloaded, never opened by the browser as a page. The type
            // is the one GIIM checked on upload, not what the uploader's browser claimed.
            var inline = download != true && AttachmentRules.CanPreview(file.ContentType);
            var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
            disposition.SetHttpFileName(file.FileName);
            http.Response.Headers.ContentDisposition = disposition.ToString();
            http.Response.Headers.CacheControl = "private, max-age=3600";   // a file never changes under its id
            // Opened on its own, a file can't run anything or reach anything, whatever is inside it.
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
            return Results.Stream(content, file.ContentType);
        }));

        group.MapPost("/{attachmentId:guid}/remove", (Guid assetId, Guid attachmentId, RemoveAttachmentRequest request,
            AttachmentService files, ICurrentUser user, CancellationToken ct) => AssetEndpoints.Handle(async () =>
        {
            await files.RemoveAsync(assetId, attachmentId, request.Reason ?? "", new ActionContext(user.Name, request.TicketNumber), ct);
            return Results.NoContent();
        }));
    }
}
