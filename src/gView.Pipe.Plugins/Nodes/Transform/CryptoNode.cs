using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using gView.Pipe.Plugins.Nodes.Files;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace gView.Pipe.Plugins.Nodes.Transform;

public enum CryptoOperation
{
    [PipeEnumValue("A checksum of text or a file, e.g. to detect changes")]
    Hash,

    [PipeEnumValue("A signature with a secret key, e.g. to check or sign webhook calls", Label = "HMAC")]
    Hmac,

    [PipeEnumValue("A new random id, e.g. 3f2b8c1e-…", Label = "UUID")]
    Uuid,

    [PipeEnumValue("Text or a file as Base64 text", Label = "Base64 encode")]
    Base64Encode,

    [PipeEnumValue("Base64 text back to plain text", Label = "Base64 decode")]
    Base64Decode
}

public enum CryptoAlgorithm
{
    [PipeEnumValue("The common choice", Label = "SHA-256")]
    Sha256,

    [PipeEnumValue("Longer and stronger", Label = "SHA-512")]
    Sha512,

    [PipeEnumValue("Only for checksums of old systems, not for security", Label = "SHA-1")]
    Sha1,

    [PipeEnumValue("Only for checksums of old systems, not for security", Label = "MD5")]
    Md5
}

public enum CryptoSource
{
    [PipeEnumValue("The text in Value")]
    Value,

    [PipeEnumValue("The content of an attachment, e.g. a file from Read File", Label = "Attachment")]
    Binary
}

public enum CryptoOutput
{
    [PipeEnumValue("Lower-case hex digits, e.g. 2cf24d…")]
    Hex,

    [PipeEnumValue("Base64 text, shorter")]
    Base64
}

public sealed class CryptoNodeParameters
{
    [PipeParameter(Label = "Operation", Order = 1, Expressions = false, Description = "What to compute")]
    public CryptoOperation Operation { get; set; } = CryptoOperation.Hash;

    [PipeParameter(Label = "Source", Order = 2, Expressions = false, ShowWhen = nameof(Operation), ShowWhenValues = ["hash", "hmac", "base64Encode"],
        Description = "Work on the text in Value or on an attachment")]
    public CryptoSource Source { get; set; } = CryptoSource.Value;

    [PipeParameter(Label = "Value", Order = 3, Placeholder = "{{ $json.body }}", ShowWhen = nameof(Operation), ShowWhenValues = ["hash", "hmac", "base64Encode", "base64Decode"],
        Description = "Text to work on, e.g. {{ $json.body }}; ignored for an attachment")]
    public string? Value { get; set; }

    [PipeParameter(Label = "Attachment name", Order = 4, Expressions = false, ShowWhen = nameof(Source), ShowWhenValues = ["binary"],
        Description = "Name of the attachment, data unless an earlier node named it differently")]
    public string BinaryProperty { get; set; } = "data";

    [PipeParameter(Label = "Algorithm", Order = 5, Expressions = false, ShowWhen = nameof(Operation), ShowWhenValues = ["hash", "hmac"],
        Description = "Hash function; SHA-256 unless a partner asks for another")]
    public CryptoAlgorithm Algorithm { get; set; } = CryptoAlgorithm.Sha256;

    [PipeParameter(Label = "Secret", Order = 6, Placeholder = "{{ $vars.WEBHOOK_SECRET }}", ShowWhen = nameof(Operation), ShowWhenValues = ["hmac"],
        Description = "The key, best from a secret variable, e.g. {{ $vars.WEBHOOK_SECRET }}")]
    public string? Secret { get; set; }

    [PipeParameter(Label = "Output", Order = 7, Expressions = false, ShowWhen = nameof(Operation), ShowWhenValues = ["hash", "hmac"],
        Description = "How the result is written")]
    public CryptoOutput Output { get; set; } = CryptoOutput.Hex;

    [Required, PipeParameter(Label = "Target field", Order = 8, Expressions = false, Description = "Field that gets the result, e.g. hash or signature")]
    public string TargetField { get; set; } = "hash";
}

[PipeNode(PipeBuiltInNodes.Crypto, "Crypto",
    Category = "Transform",
    Description = "Hashes text or attachments (SHA-256, SHA-512, SHA-1, MD5), signs them with HMAC, creates UUIDs and encodes or decodes Base64. Writes the result into a field of each item.")]
[PipeNodeExamples]
public sealed class CryptoNode : PipeItemNodeBase<CryptoNodeParameters>
{
    protected override async Task<PipeItem?> ProcessItemAsync(PipeItem item, CryptoNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        string result = parameters.Operation switch
        {
            CryptoOperation.Uuid => Guid.NewGuid().ToString(),
            CryptoOperation.Base64Encode => Convert.ToBase64String(await InputAsync(item, parameters, context, cancellationToken)),
            CryptoOperation.Base64Decode => Decode(parameters.Value),
            CryptoOperation.Hmac => Encode(Hmac(parameters, await InputAsync(item, parameters, context, cancellationToken)), parameters.Output),
            _ => Encode(await HashAsync(item, parameters, context, cancellationToken), parameters.Output)
        };

        var output = item.Clone();
        PipeJsonPath.Set(output.Json, parameters.TargetField, result);
        return output;
    }

    static private async Task<byte[]> InputAsync(PipeItem item, CryptoNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        if (parameters.Source == CryptoSource.Value)
        {
            return Encoding.UTF8.GetBytes(parameters.Value ?? "");
        }
        await using var content = await context.BinaryStore.OpenReadAsync(PipeFileFormatNodes.Attachment(item, parameters.BinaryProperty), cancellationToken);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Attachments are hashed as a stream, so large files don't need to fit into memory.</summary>
    static private async Task<byte[]> HashAsync(PipeItem item, CryptoNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        using var algorithm = IncrementalHash.CreateHash(HashName(parameters.Algorithm));
        if (parameters.Source == CryptoSource.Value)
        {
            algorithm.AppendData(Encoding.UTF8.GetBytes(parameters.Value ?? ""));
        }
        else
        {
            await using var content = await context.BinaryStore.OpenReadAsync(PipeFileFormatNodes.Attachment(item, parameters.BinaryProperty), cancellationToken);
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                algorithm.AppendData(buffer, 0, read);
            }
        }
        return algorithm.GetHashAndReset();
    }

    static private byte[] Hmac(CryptoNodeParameters parameters, byte[] data)
    {
        if (string.IsNullOrEmpty(parameters.Secret))
        {
            throw new PipeException("HMAC needs a secret");
        }
        using var algorithm = IncrementalHash.CreateHMAC(HashName(parameters.Algorithm), Encoding.UTF8.GetBytes(parameters.Secret));
        algorithm.AppendData(data);
        return algorithm.GetHashAndReset();
    }

    static private HashAlgorithmName HashName(CryptoAlgorithm algorithm) => algorithm switch
    {
        CryptoAlgorithm.Sha512 => HashAlgorithmName.SHA512,
        CryptoAlgorithm.Sha1 => HashAlgorithmName.SHA1,
        CryptoAlgorithm.Md5 => HashAlgorithmName.MD5,
        _ => HashAlgorithmName.SHA256
    };

    static private string Encode(byte[] bytes, CryptoOutput output)
        => output == CryptoOutput.Base64 ? Convert.ToBase64String(bytes) : Convert.ToHexStringLower(bytes);

    static private string Decode(string? base64)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64?.Trim() ?? ""));
        }
        catch (FormatException)
        {
            throw new PipeException("The value isn't valid Base64");
        }
    }
}
