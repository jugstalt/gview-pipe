using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Nodes;
using System.ComponentModel.DataAnnotations;

namespace gView.Pipe.Plugins.Sftp;

[PipeCredentialType(SftpCredential.TypeId, "SFTP Server",
    Description = "Server, login and the fingerprint of its host key for the SFTP node")]
public sealed class SftpCredential
{
    public const string TypeId = "90e8ec4b-32b6-4a55-9798-e04a3369b8a3";

    [Required, PipeParameter(Label = "Host", Order = 1)]
    public string Host { get; set; } = "";

    [PipeParameter(Label = "Port", Order = 2)]
    [Range(1, 65535)]
    public int Port { get; set; } = 22;

    [Required, PipeParameter(Label = "User name", Order = 3)]
    public string UserName { get; set; } = "";

    [Secret, PipeParameter(Label = "Password", Order = 4, Description = "A password, a private key, or both (the key is tried first)")]
    public string? Password { get; set; }

    [Secret, PipeParameter(Label = "Private key", Order = 5, Multiline = true, Description = "PEM or OpenSSH format")]
    public string? PrivateKey { get; set; }

    [Secret, PipeParameter(Label = "Passphrase", Order = 6, Description = "Of the private key, if it has one")]
    public string? Passphrase { get; set; }

    [Required, PipeParameter(Label = "Host key fingerprint", Order = 7, Placeholder = "SHA256:…",
        Description = "SHA-256 fingerprint of the server's host key. A server with another fingerprint is refused; the error shows the one it presented")]
    public string HostKeyFingerprint { get; set; } = "";
}
