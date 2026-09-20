namespace LaptopGuardian.Agent.Identity;

public interface ICredentialProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}
