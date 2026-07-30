namespace RetreatUI.Launcher.Services;

public readonly record struct AddonVersion(
    int Major,
    int Minor,
    int Patch,
    bool IsPrerelease,
    string Prerelease) : IComparable<AddonVersion>
{
    public static AddonVersion Parse(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().TrimStart('v', 'V');
        string[] split = normalized.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
        string[] core = split.Length > 0
            ? split[0].Split('.', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

        int major = core.Length > 0 && int.TryParse(core[0], out int ma) ? ma : 0;
        int minor = core.Length > 1 && int.TryParse(core[1], out int mi) ? mi : 0;
        int patch = core.Length > 2 && int.TryParse(core[2], out int pa) ? pa : 0;
        string prerelease = split.Length > 1 ? split[1] : string.Empty;

        return new AddonVersion(major, minor, patch, prerelease.Length > 0, prerelease);
    }

    public static int Compare(string? left, string? right) =>
        Parse(left).CompareTo(Parse(right));

    public int CompareTo(AddonVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        if (IsPrerelease != other.IsPrerelease)
        {
            return IsPrerelease ? -1 : 1;
        }

        return IsPrerelease
            ? ComparePrerelease(Prerelease, other.Prerelease)
            : 0;
    }

    private static int ComparePrerelease(string leftValue, string rightValue)
    {
        string[] left = leftValue.Split('.', StringSplitOptions.RemoveEmptyEntries);
        string[] right = rightValue.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int count = Math.Max(left.Length, right.Length);

        for (int index = 0; index < count; index++)
        {
            if (index >= left.Length) return -1;
            if (index >= right.Length) return 1;

            string leftIdentifier = left[index];
            string rightIdentifier = right[index];
            bool leftNumeric = int.TryParse(leftIdentifier, out int leftNumber);
            bool rightNumeric = int.TryParse(rightIdentifier, out int rightNumber);

            int result;
            if (leftNumeric && rightNumeric)
            {
                result = leftNumber.CompareTo(rightNumber);
            }
            else if (leftNumeric != rightNumeric)
            {
                result = leftNumeric ? -1 : 1;
            }
            else
            {
                result = string.Compare(
                    leftIdentifier,
                    rightIdentifier,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }
}
