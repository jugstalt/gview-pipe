using gView.Pipe.AspNetCore.Authentication;

namespace gView.Pipe.AspNetCore.Tests.Authentication;

public class PipePasswordHasherTests
{
    [Fact]
    public void Hash_VerifiesOnlyTheSamePassword()
    {
        var hash = PipePasswordHasher.Hash("s3cret");

        Assert.StartsWith("pbkdf2-sha256$", hash);
        Assert.True(PipePasswordHasher.Verify("s3cret", hash));
        Assert.False(PipePasswordHasher.Verify("S3cret", hash));
    }

    [Fact]
    public void Hash_IsSalted()
    {
        Assert.NotEqual(PipePasswordHasher.Hash("same"), PipePasswordHasher.Hash("same"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("pbkdf2-sha256$x$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$1000$not base64$AAAA")]
    public void Verify_RejectsMalformedHashes(string hash)
    {
        Assert.False(PipePasswordHasher.Verify("anything", hash));
    }

    [Fact]
    public void Verify_User_PrefersTheHash()
    {
        var user = new PipeFormsUser { Name = "admin", Password = "plain", PasswordHash = PipePasswordHasher.Hash("hashed") };

        Assert.True(PipePasswordHasher.Verify(user, "hashed"));
        Assert.False(PipePasswordHasher.Verify(user, "plain"));
    }

    [Fact]
    public void Verify_User_WithoutPassword_NeverMatches()
    {
        Assert.False(PipePasswordHasher.Verify(new PipeFormsUser { Name = "admin" }, ""));
    }
}
