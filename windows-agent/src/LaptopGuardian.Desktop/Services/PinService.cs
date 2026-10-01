using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LaptopGuardian.Desktop.Services;

/// <summary>
/// Manages PIN storage and verification using DPAPI and PBKDF2.
/// </summary>
public sealed class PinService
{
    private static readonly string PinDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopGuardian", "pin.dat");

    private const int SaltSize = 32;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private const int MinPinLength = 4;
    private const int MaxPinLength = 8;

    private readonly object _lockObj = new();
    private int _failedAttempts;
    private DateTime _lockoutUntil;

    /// <summary>
    /// Checks if a PIN is currently set.
    /// </summary>
    public bool HasPin
    {
        get
        {
            lock (_lockObj)
            {
                return File.Exists(PinDataPath);
            }
        }
    }

    /// <summary>
    /// Checks if the service is currently locked out due to too many failed attempts.
    /// </summary>
    public bool IsLockedOut
    {
        get
        {
            lock (_lockObj)
            {
                // Clear lockout if time has passed
                if (DateTime.UtcNow >= _lockoutUntil && _lockoutUntil > DateTime.MinValue)
                {
                    _lockoutUntil = DateTime.MinValue;
                    _failedAttempts = 0;
                }
                return DateTime.UtcNow < _lockoutUntil;
            }
        }
    }

    /// <summary>
    /// Gets remaining lockout time in seconds.
    /// </summary>
    public int LockoutRemainingSeconds
    {
        get
        {
            lock (_lockObj)
            {
                if (DateTime.UtcNow >= _lockoutUntil) return 0;
                return (int)(_lockoutUntil - DateTime.UtcNow).TotalSeconds;
            }
        }
    }

    /// <summary>
    /// Gets the number of failed attempts.
    /// </summary>
    public int FailedAttempts
    {
        get
        {
            lock (_lockObj)
            {
                return _failedAttempts;
            }
        }
    }

    /// <summary>
    /// Sets a new PIN. Throws if PIN is invalid.
    /// </summary>
    public void SetPin(string pin)
    {
        ValidatePin(pin);

        lock (_lockObj)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = HashPin(pin, salt);

            // Combine salt + hash
            var data = new byte[SaltSize + HashSize];
            Buffer.BlockCopy(salt, 0, data, 0, SaltSize);
            Buffer.BlockCopy(hash, 0, data, SaltSize, HashSize);

            // Protect with DPAPI
            var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

            // Save to file
            var dir = Path.GetDirectoryName(PinDataPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(PinDataPath, protectedData);

            // Reset failed attempts
            _failedAttempts = 0;
            _lockoutUntil = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Verifies a PIN. Returns true if correct.
    /// </summary>
    public bool VerifyPin(string pin)
    {
        if (string.IsNullOrEmpty(pin))
            return false;

        lock (_lockObj)
        {
            // Check lockout
            if (IsLockedOut)
                return false;

            if (!HasPin)
                return false;

            try
            {
                // Load and unprotect
                var protectedData = File.ReadAllBytes(PinDataPath);
                var data = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);

                if (data.Length != SaltSize + HashSize)
                    return false;

                // Extract salt and stored hash
                var salt = new byte[SaltSize];
                var storedHash = new byte[HashSize];
                Buffer.BlockCopy(data, 0, salt, 0, SaltSize);
                Buffer.BlockCopy(data, SaltSize, storedHash, 0, HashSize);

                // Hash the input PIN
                var inputHash = HashPin(pin, salt);

                // Compare
                var isMatch = CryptographicOperations.FixedTimeEquals(inputHash, storedHash);

                if (isMatch)
                {
                    // Reset on success
                    _failedAttempts = 0;
                    _lockoutUntil = DateTime.MinValue;
                    return true;
                }
                else
                {
                    // Track failed attempt
                    _failedAttempts++;
                    if (_failedAttempts >= 5)
                    {
                        _lockoutUntil = DateTime.UtcNow.AddSeconds(30);
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Removes the PIN.
    /// </summary>
    public void RemovePin()
    {
        lock (_lockObj)
        {
            if (File.Exists(PinDataPath))
                File.Delete(PinDataPath);

            _failedAttempts = 0;
            _lockoutUntil = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Resets failed attempts and lockout. For testing/admin purposes.
    /// </summary>
    public void ResetLockout()
    {
        lock (_lockObj)
        {
            _failedAttempts = 0;
            _lockoutUntil = DateTime.MinValue;
        }
    }

    private static void ValidatePin(string pin)
    {
        if (string.IsNullOrEmpty(pin))
            throw new ArgumentException("PIN cannot be empty.", nameof(pin));

        if (pin.Length < MinPinLength)
            throw new ArgumentException($"PIN must be at least {MinPinLength} digits.", nameof(pin));

        if (pin.Length > MaxPinLength)
            throw new ArgumentException($"PIN cannot exceed {MaxPinLength} digits.", nameof(pin));

        if (!pin.All(char.IsDigit))
            throw new ArgumentException("PIN must contain only digits.", nameof(pin));
    }

    private static byte[] HashPin(string pin, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);
    }
}
