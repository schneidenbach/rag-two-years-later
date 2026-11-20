using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Microsoft.Extensions.AI;

namespace Lakeshore.Agents.Knowledge;

/// <summary>
/// Document store (Blob) -> chunk by "##" section -> embed -> push to the AI Search index -> knowledge source -> knowledge base.
/// </summary>
public sealed partial class DocumentIngestor(LakeshoreAgents a, string storageAccountName, string container = "facilities-docs")
{
    public async Task UploadFolderAsync(string folder, Action<string>? log = null, CancellationToken ct = default)
    {
        var cc = Container();
        await cc.CreateIfNotExistsAsync(cancellationToken: ct);
        foreach (var f in Directory.GetFiles(folder, "*.md"))
        {
            await cc.GetBlobClient(Path.GetFileName(f)).UploadAsync(f, overwrite: true, ct);
            log?.Invoke($"uploaded {Path.GetFileName(f)}");
        }
    }

    public async Task<int> IndexFromBlobAsync(Action<string>? log = null, CancellationToken ct = default)
    {
        await a.KnowledgeBase.CreateOrUpdateIndexAsync(ct: ct);
        log?.Invoke("index ready");
        var cc = Container();
        var chunks = new List<JsonObject>();
        await foreach (var item in cc.GetBlobsAsync(cancellationToken: ct))
        {
            var blob = cc.GetBlobClient(item.Name);
            var text = (await blob.DownloadContentAsync(ct)).Value.Content.ToString();
            var docChunks = Chunk(text, blob.Uri.ToString()).ToList();
            chunks.AddRange(docChunks);
            log?.Invoke($"{item.Name}: {docChunks.Count} chunks");
        }
        foreach (var batch in chunks.Chunk(64))
        {
            var embs = await a.Embeddings.GenerateAsync(batch.Select(c => $"{c["title"]} - {c["section"]}\n{c["content"]}"), cancellationToken: ct);
            for (int i = 0; i < batch.Length; i++)
                batch[i]["content_vector"] = new JsonArray(embs[i].Vector.ToArray().Select(f => (JsonNode)f).ToArray());
        }
        await a.KnowledgeBase.UploadAsync(chunks, ct);
        log?.Invoke($"pushed {chunks.Count} chunks");
        await a.KnowledgeBase.CreateOrUpdateKnowledgeSourceAsync(ct);
        log?.Invoke("knowledge source ready");
        await a.KnowledgeBase.CreateOrUpdateKnowledgeBaseAsync(ct);
        log?.Invoke("knowledge base ready");
        return chunks.Count;
    }

    BlobContainerClient Container() =>
        new BlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"), a.Credential).GetBlobContainerClient(container);

    /// <summary>Parses YAML-ish frontmatter and splits the body on "## " headings. Each chunk carries its doc title.</summary>
    public static IEnumerable<JsonObject> Chunk(string markdown, string sourceUrl)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = markdown;
        var fm = Frontmatter().Match(markdown);
        if (fm.Success)
        {
            foreach (var line in fm.Groups[1].Value.Split('\n'))
            {
                var idx = line.IndexOf(':');
                if (idx > 0) meta[line[..idx].Trim()] = line[(idx + 1)..].Trim().Trim('"', '\'');
            }
            body = markdown[fm.Length..];
        }
        var docId = meta.GetValueOrDefault("doc_id") ?? Path.GetFileNameWithoutExtension(new Uri(sourceUrl).LocalPath);
        var title = meta.GetValueOrDefault("title") ?? docId;

        var sections = new List<(string Heading, StringBuilder Text)> { ("Overview", new StringBuilder()) };
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ")) sections.Add((line[3..].Trim(), new StringBuilder()));
            else if (!line.StartsWith("# ")) sections[^1].Text.AppendLine(line);
        }
        int n = 0;
        foreach (var (heading, sb) in sections)
        {
            var content = sb.ToString().Trim();
            if (content.Length < 40) continue;
            yield return new JsonObject
            {
                ["id"] = Key($"{docId}-{n++:00}"),
                ["doc_id"] = docId,
                ["title"] = title,
                ["section"] = heading,
                ["doc_type"] = meta.GetValueOrDefault("doc_type") ?? "document",
                ["effective_date"] = meta.GetValueOrDefault("effective_date") ?? "",
                ["source_url"] = sourceUrl,
                ["content"] = content,
            };
        }
    }

    static string Key(string s) => Regex.Replace(s, "[^A-Za-z0-9_\\-=]", "-");

    [GeneratedRegex(@"\A---\s*\n(.*?)\n---\s*\n", RegexOptions.Singleline)]
    private static partial Regex Frontmatter();
}
