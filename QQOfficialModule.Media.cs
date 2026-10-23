using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;

namespace Alife.QQOfficial;

/// <summary>
/// 富媒体与素材。官方两条上传路线在这里都实现了，并且**按大小自动选**：
/// 整文件路线（/files 直接给 url 或 base64）与分片路线（upload_prepare → 逐片 PUT 预签名 URL → upload_part_finish → /files 带 upload_id）。
/// 平台标的软限制是图 20MB / 视频 30MB / 语音 20MB / 文件 200MB，**超软限制会被降级成文件类型**，超 200MB 直接报错。
/// 另外单独一条「素材管理」（2026-10-21 新增）：图片传上去拿 qqbot-media:// 引用写进 markdown，单张 ≤10MB、每机器人 100 张、90 天滑动过期。
/// </summary>
public partial class QQOfficialModule
{
    const long chunkSoftLimitBytes = 6 * 1024 * 1024;
    const long hardLimitBytes = 200L * 1024 * 1024;
    const long md5HeadBytes = 10002432;
    const long materialImageMaxBytes = 10L * 1024 * 1024;
    /// <summary>pcm 采样率：必须与 tools/to_silk.py 里 pilk 的 pcm_rate 一致，否则语速变调</summary>
    const int pcmRate = 24000;
    const string mediaPreparePath = "/upload_prepare";
    const string mediaPartFinishPath = "/upload_part_finish";
    const string materialBase = "/v2/media-images";
    static readonly string[] materialTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    [XmlFunction(FunctionMode.OneShot)]
    [Description("上传一张本地图片当 markdown 素材，返回可以直接写进 markdown 的 qqbot-media:// 引用与 image_id。"
        + "限制：png/jpeg/gif/webp、单张 ≤10MB、每个机器人最多 100 张（用 QQDeleteMarkdownImage 释放）、自最后一次被引用起 90 天内有效。"
        + "markdown 里写法：![说明 #宽px #高px](qqbot-media://image/v1/…)")]
    public async Task QQUploadMarkdownImage(string localPath)
    {
        string path = Need(localPath, "localPath");
        FileInfo file = new(path);
        if (file.Exists == false)
        {
            throw new Exception("找不到文件：" + path);
        }
        if (file.Length > materialImageMaxBytes)
        {
            throw new Exception("素材图片最大 " + (materialImageMaxBytes / 1024 / 1024).ToString(CultureInfo.InvariantCulture)
                + "MB，这个文件 " + (file.Length / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + "MB");
        }
        string contentType = GuessImageContentType(file.Extension);
        byte[] bytes = File.ReadAllBytes(path);
        JsonElement prepared = await RequireRest().PostAsync(materialBase + "/prepare", new JsonObject
        {
            ["file_name"] = file.Name,
            ["file_size"] = file.Length,
            ["content_type"] = contentType,
            ["md5"] = HashHex(MD5.Create(), bytes),
            ["sha1"] = HashHex(SHA1.Create(), bytes)
        }, DestroyCancellationToken);
        string uploadId = QQJson.Text(prepared, "upload_id");
        string presigned = QQJson.Text(prepared, "presigned_url");
        if (uploadId.Length == 0 || presigned.Length == 0)
        {
            throw new Exception("prepare 没返回 upload_id/presigned_url：" + QQRestClient.Shorten(prepared.ToString()));
        }
        string etag = await PutToPresignedAsync(presigned, bytes, contentType);
        JsonElement done = await RequireRest().PostAsync(materialBase + "/complete", new JsonObject
        {
            ["upload_id"] = uploadId,
            ["md5"] = etag
        }, DestroyCancellationToken);
        interactor.Poke("素材已上传：" + QQJson.Text(done, "image_id")
            + "\nmarkdown 引用：" + QQJson.Text(done, "url")
            + "\n有效期 " + (QQJson.Integer(done, "ttl") / 86400.0).ToString("0", CultureInfo.InvariantCulture) + " 天（每次被消息引用后自动顺延）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("列当前机器人手里有效的素材图片（按上传时间倒序，游标翻页），返回 image_id、qqbot-media:// 引用、过期时间。"
        + "响应里的 preview_url 是短时效的临时预览链接，别存下来当永久地址用")]
    public async Task QQMarkdownImages(string cursor = "")
    {
        JsonElement body = await RequireRest().GetAsync(materialBase + "?cursor=" + Uri.EscapeDataString(cursor.Trim()), DestroyCancellationToken);
        List<JsonElement> images = QQJson.Array(body, "images").ToList();
        List<string> lines = new()
        {
            "素材图片 " + images.Count.ToString(CultureInfo.InvariantCulture) + " 条，平台计总数 "
            + QQJson.Integer(body, "total").ToString(CultureInfo.InvariantCulture) + " 张（上限 100 张）"
            + (QQJson.Text(body, "next_cursor").Length > 0 ? "，还有下一页" : "")
        };
        foreach (JsonElement image in images)
        {
            lines.Add("- " + QQJson.Text(image, "file_name", "（无名）") + " | image_id=" + QQJson.Text(image, "image_id")
                + " | 过期于 " + FromUnix(QQJson.Integer(image, "expires_at"))
                + "\n  " + QQJson.Text(image, "url"));
        }
        interactor.Poke(string.Join("\n", lines));
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除一张素材图片、释放 100 张配额。删除后引用链接立即失效，但**已经发出去的消息不受影响**（平台在发送时已转存副本）。"
        + "删不存在的 image_id 平台回 40035008")]
    public async Task QQDeleteMarkdownImage(string imageId)
    {
        await RequireRest().DeleteAsync(materialBase + "/" + Uri.EscapeDataString(Need(imageId, "imageId")) + "/delete", DestroyCancellationToken);
        interactor.Poke("素材 " + imageId.Trim() + " 已删除，配额已释放");
    }

    /// <summary>整文件与分片两条路线的分界：小文件走 base64/url，大文件走分片，免得把几十 MB 的 base64 塞进一个请求</summary>
    internal async Task<string> UploadMediaAsync(bool isGroup, string openid, int fileType, string localPath, string url)
    {
        string path = localPath.Trim();
        if (url.Trim().Length > 0)
        {
            JsonElement body = await RequireRest().PostAsync(MediaFilesPath(isGroup, openid), new JsonObject
            {
                ["file_type"] = fileType,
                ["url"] = url.Trim(),
                ["srv_send_msg"] = false
            }, DestroyCancellationToken);
            return RequireFileInfo(body);
        }
        if (path.Length == 0)
        {
            throw new Exception("localPath 与 url 至少给一个");
        }
        FileInfo file = new(path);
        if (file.Exists == false)
        {
            throw new Exception("找不到文件：" + path);
        }
        string voiceProblem = QQSendPolish.VoiceProblem(fileType, path, Configuration.SilkEncoderPath, Configuration.AllowNonSilkVoice);
        if (voiceProblem.Length > 0)
        {
            throw new Exception(voiceProblem);
        }
        if (fileType == 3)
        {
            file = await ConvertToSilkAsync(file);
        }
        if (file.Length > hardLimitBytes)
        {
            throw new Exception("超过平台硬限制 200MB（" + (file.Length / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + "MB）");
        }
        if (file.Length <= chunkSoftLimitBytes)
        {
            JsonObject simple = new()
            {
                ["file_type"] = fileType,
                ["file_data"] = Convert.ToBase64String(File.ReadAllBytes(path)),
                ["srv_send_msg"] = false
            };
            // 语音绝不能带 file_name：带了平台会按文件卡片发，语音条就没了
            if (fileType != 3)
            {
                simple["file_name"] = file.Name;
            }
            return RequireFileInfo(await RequireRest().PostAsync(MediaFilesPath(isGroup, openid), simple, DestroyCancellationToken));
        }
        return await UploadInChunksAsync(isGroup, openid, fileType, file);
    }

    string MediaFilesPath(bool isGroup, string openid) => (isGroup ? groupsPath : usersPath) + Uri.EscapeDataString(openid) + "/files";

    /// <summary>分片四步：prepare 拿 upload_id/block_size/预签名 URL → 逐片 PUT → 逐片 part_finish → /files 带 upload_id 合并拿 file_info。
    /// 路径参数名平台写的是 group_id/user_id，但描述明确「群 OpenID/用户 OpenID」，所以传的就是 openid</summary>
    async Task<string> UploadInChunksAsync(bool isGroup, string openid, int fileType, FileInfo file)
    {
        byte[] bytes = File.ReadAllBytes(file.FullName);
        string headHash = HashHex(MD5.Create(), bytes.Take((int)Math.Min(md5HeadBytes, bytes.Length)).ToArray());
        string preparePath = (isGroup ? groupsPath : usersPath) + Uri.EscapeDataString(openid) + mediaPreparePath;
        JsonElement prepared = await RequireRest().PostAsync(preparePath, new JsonObject
        {
            ["file_type"] = fileType,
            ["file_size"] = bytes.Length.ToString(CultureInfo.InvariantCulture),
            ["file_name"] = file.Name,
            ["md5"] = HashHex(MD5.Create(), bytes),
            ["sha1"] = HashHex(SHA1.Create(), bytes),
            ["md5_10m"] = headHash
        }, DestroyCancellationToken);
        string uploadId = QQJson.Text(prepared, "upload_id");
        long blockSize = QQJson.Integer(prepared, "block_size", 5 * 1024 * 1024);
        List<JsonElement> parts = QQJson.Array(prepared, "parts").ToList();
        if (uploadId.Length == 0 || parts.Count == 0)
        {
            throw new Exception("预上传没给 upload_id 或分片列表：" + QQRestClient.Shorten(prepared.ToString()));
        }
        string finishPath = (isGroup ? groupsPath : usersPath) + Uri.EscapeDataString(openid) + mediaPartFinishPath;
        foreach (JsonElement part in parts)
        {
            int index = (int)QQJson.Integer(part, "index");
            string presigned = QQJson.Text(part, "presigned_url");
            if (presigned.Length == 0)
            {
                throw new Exception("分片 " + index.ToString(CultureInfo.InvariantCulture) + " 没有预签名 URL");
            }
            int start = index * (int)blockSize;
            int length = Math.Min((int)blockSize, bytes.Length - start);
            if (length <= 0)
            {
                continue;
            }
            byte[] chunk = bytes.Skip(start).Take(length).ToArray();
            string etag = await PutToPresignedAsync(presigned, chunk, "application/octet-stream");
            await RequireRest().PostAsync(finishPath, new JsonObject
            {
                ["upload_id"] = uploadId,
                ["part_index"] = index,
                ["block_size"] = length.ToString(CultureInfo.InvariantCulture),
                ["md5"] = etag.Length > 0 ? etag : HashHex(MD5.Create(), chunk)
            }, DestroyCancellationToken);
        }
        JsonElement merged = await RequireRest().PostAsync(MediaFilesPath(isGroup, openid), new JsonObject
        {
            ["file_type"] = fileType,
            ["upload_id"] = uploadId,
            ["url"] = "",
            ["srv_send_msg"] = false
        }, DestroyCancellationToken);
        return RequireFileInfo(merged);
    }

    static string RequireFileInfo(JsonElement uploaded)
    {
        string fileInfo = QQJson.Text(uploaded, "file_info");
        if (fileInfo.Length == 0)
        {
            throw new Exception("上传没拿到 file_info：" + QQRestClient.Shorten(uploaded.ToString()));
        }
        return fileInfo;
    }

    /// <summary>直传 COS：Content-Type 必须与申请时声明的一致，否则 COS 回 403；返回头里的 ETag（去引号）就是下一步要的 MD5</summary>
    static async Task<string> PutToPresignedAsync(string presignedUrl, byte[] payload, string contentType)
    {
        using HttpClient client = new();
        using ByteArrayContent content = new(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using HttpRequestMessage request = new(HttpMethod.Put, presignedUrl) { Content = content };
        using HttpResponseMessage response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode == false)
        {
            throw new Exception("分片直传失败 HTTP " + (int)response.StatusCode + "（Content-Type 与申请时不一致会被 COS 拒 403）");
        }
        return response.Headers.ETag == null ? "" : response.Headers.ETag.ToString().Trim('"');
    }

    static string HashHex(HashAlgorithm algorithm, byte[] payload)
    {
        using (algorithm)
        {
            return string.Concat(algorithm.ComputeHash(payload).Select(item => item.ToString("x2")));
        }
    }

    static string GuessImageContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => throw new Exception("素材图片只收 " + string.Join("/", materialTypes))
    };

    static string FromUnix(long seconds) => seconds <= 0
        ? "?"
        : DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// 语音条要 silk，而 ffmpeg 只能**解码** silk 不能编码，所以链路是：ffmpeg 把任意音频解成 pcm → 交给用户配的编码器出 silk。
    /// 没配编码器时上面已经拦住了（不会悄悄发成文件卡片）；这里只负责真跑，任何一步失败都把工具的 stderr 原样报出来。
    /// </summary>
    async Task<FileInfo> ConvertToSilkAsync(FileInfo source)
    {
        string encoder = Configuration.SilkEncoderPath.Trim();
        if (encoder.Length == 0)
        {
            return source;
        }
        if (QQSendPolish.EncoderArgsUsable(Configuration.SilkEncoderArgs) == false)
        {
            throw new Exception("配置「语音·编码器参数」里必须同时有 {in} 与 {out} 两个占位符");
        }
        string ffmpeg = FindTool("ffmpeg.exe");
        if (ffmpeg.Length == 0)
        {
            throw new Exception("转 silk 需要 ffmpeg 先把音频解成 pcm，但 PATH 上找不到 ffmpeg.exe");
        }
        string work = Path.Combine(Path.GetTempPath(), "qqofficial_voice_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        string pcm = Path.Combine(work, "audio.pcm");
        string silk = Path.Combine(work, Path.GetFileNameWithoutExtension(source.Name) + ".silk");
        int cap = Math.Clamp(Configuration.VoiceMaxSeconds, 5, 3600);
        (string program, string arguments)[] steps =
        [
            (ffmpeg, "-y -i \"" + source.FullName + "\" -t " + cap.ToString(CultureInfo.InvariantCulture)
                + " -f s16le -ar " + pcmRate.ToString(CultureInfo.InvariantCulture) + " -ac 1 \"" + pcm + "\""),
            (encoder, Configuration.SilkEncoderArgs
                .Replace("{in}", "\"" + pcm + "\"", StringComparison.Ordinal)
                .Replace("{out}", "\"" + silk + "\"", StringComparison.Ordinal))
        ];
        foreach ((string program, string arguments) in steps)
        {
            (int exitCode, string error) = await RunToolAsync(program, arguments);
            if (exitCode != 0)
            {
                throw new Exception(Path.GetFileName(program) + " 转码失败（退出码 " + exitCode.ToString(CultureInfo.InvariantCulture)
                    + "）：" + QQRestClient.Shorten(error));
            }
        }
        FileInfo result = new(silk);
        if (result.Exists == false || result.Length == 0)
        {
            throw new Exception("编码器跑完了但没产出 silk 文件，检查「语音·silk 编码器」的参数写法");
        }
        DetailLog("语音已转 silk：" + result.Length.ToString(CultureInfo.InvariantCulture) + " 字节");
        return result;
    }

    static string FindTool(string executable)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (directory.Trim().Length == 0)
            {
                continue;
            }
            string candidate = Path.Combine(directory.Trim(), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return "";
    }

    static async Task<(int ExitCode, string Error)> RunToolAsync(string program, string arguments)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = program,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        process.Start();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(120));
        string error = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, error);
    }
}
