// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using System.IO;
using System.Net.Http;

namespace PixelEditor.Imaging;

/// <summary>
/// Downloads a picture by an http(s) link into a file for further processing (the "picture from the internet to pixel art" template).
/// Limits: only http and https, no more than maxBytes, the response must be an image (image/*), a timeout. The link is chosen by the user or the agent;
/// the picture's license is checked by whoever chose it (use free sources).
/// </summary>
public static class ImageFetcher
{
    public const long DefaultMaxBytes = 15 * 1024 * 1024;

    public sealed record FetchResult(string File, long Bytes, string ContentType, string Url);

    public static FetchResult Fetch(string url, string output, long maxBytes = DefaultMaxBytes, int timeoutSeconds = 30)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException(CoreText.T("Нужна ссылка http:// или https://"));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PixelEditor-fetch/1.0");
        using var response = http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(CoreText.F("По ссылке не изображение (тип «{0}»). Нужна прямая ссылка на файл картинки", type));
        if (response.Content.Headers.ContentLength is long len && len > maxBytes)
            throw new InvalidOperationException(CoreText.F("Файл слишком большой: {0} МБ, лимит {1} МБ", len / 1024 / 1024, maxBytes / 1024 / 1024));

        using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int n;
        while ((n = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, n);
            if (buffer.Length > maxBytes) throw new InvalidOperationException(CoreText.F("Файл больше лимита {0} МБ", maxBytes / 1024 / 1024));
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(output, buffer.ToArray());
        // make sure the file really reads as a picture (otherwise report at once, not at the processing step)
        try { ImageIO.Load(output); }
        catch (Exception ex)
        {
            File.Delete(output);
            throw new InvalidOperationException(CoreText.T("Скачанный файл не открывается как картинка: ") + ex.Message);
        }
        return new FetchResult(Path.GetFullPath(output), buffer.Length, type, url);
    }
}
