namespace AltomateHR.Api.Modules.Employees;

// The house convention for a new employee's first password: their email
// address followed by their birthday as MMDD. Born 23 November →
// "aisyah@example.com1123".
//
// A rule rather than a random string because of how it is delivered: the
// welcome email tells the employee "your password is your email followed by
// your birthday as MMDD" and nothing else. Nobody has to relay a specific
// secret, and an admin can always tell someone what theirs is without looking
// it up. That is also its weakness — anyone holding the staff list and a date
// of birth can derive it — so it is a FIRST password, not a lasting one.
//
// Ported from the legacy lib/auth/password.ts, including the null return: a
// missing or unparseable date of birth must stop the caller rather than seed
// something unusable.
public static class DefaultPassword
{
    // Null when the date of birth is missing. Callers must refuse the row
    // instead of inventing a password nobody can be told how to guess.
    public static string? For(string? email, DateTime? dateOfBirth)
    {
        if (string.IsNullOrWhiteSpace(email) || dateOfBirth is null) return null;
        return $"{email.Trim()}{dateOfBirth.Value:MMdd}";
    }
}
