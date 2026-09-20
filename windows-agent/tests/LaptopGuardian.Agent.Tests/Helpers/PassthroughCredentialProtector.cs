using LaptopGuardian.Agent.Identity;

namespace LaptopGuardian.Agent.Tests;

internal sealed class PassthroughCredentialProtector : ICredentialProtector
{
    public byte[] Protect(byte[] data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ 0xAA);
        return result;
    }

    public byte[] Unprotect(byte[] data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ 0xAA);
        return result;
    }
}
