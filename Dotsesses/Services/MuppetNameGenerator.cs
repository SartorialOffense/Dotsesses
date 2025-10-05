namespace Dotsesses.Services;

using Dotsesses.Models;

/// <summary>
/// Generates unique whimsical MuppetNames for students.
/// Uses constant seed for reproducibility.
/// </summary>
public class MuppetNameGenerator
{
    private const int Seed = 42;

    private static readonly string[] Emojis = new[]
    {
        "🐸", "🐷", "🐻", "🐔", "🐶", "🐀", "🐙", "🦐", "🎭", "🎪",
        "🎨", "🎬", "🎤", "🎸", "🥁", "🎺", "🎹", "🎻", "🍪", "🍕",
        "🍔", "🌮", "🎂", "🎈", "🎉", "⭐", "✨", "🔴", "🔵", "🟢",
        "🟡", "🟣", "🟠", "❤️", "💙", "💚", "💛", "💜", "🧡", "🎯",
        "🏆", "👑", "🌟", "💫", "🌈", "🦋", "🌸", "🌺", "🌻", "🌼"
    };

    /// <summary>
    /// Generates unique MuppetNameInfo for each student ID.
    /// </summary>
    /// <param name="studentIds">Student IDs ordered consistently</param>
    /// <returns>Dictionary mapping student ID to MuppetNameInfo</returns>
    public Dictionary<int, MuppetNameInfo> Generate(IEnumerable<int> studentIds)
    {
        ArgumentNullException.ThrowIfNull(studentIds);

        var random = new Random(Seed);
        var usedNames = new HashSet<string>();
        var result = new Dictionary<int, MuppetNameInfo>();

        var availableNames = MuppetNames.Names.ToList();

        foreach (var id in studentIds)
        {
            string name;
            string emojis;

            // Find unique name
            do
            {
                if (availableNames.Count == 0)
                {
                    // Ran out of unique names - start reusing with suffixes
                    name = MuppetNames.Names[random.Next(MuppetNames.Names.Length)] +
                           $" {random.Next(1, 1000)}";
                }
                else
                {
                    int index = random.Next(availableNames.Count);
                    name = availableNames[index];
                    availableNames.RemoveAt(index);
                }
            }
            while (usedNames.Contains(name));

            usedNames.Add(name);

            // Generate 1-3 random emojis
            int emojiCount = random.Next(1, 4);
            var selectedEmojis = new List<string>();
            for (int i = 0; i < emojiCount; i++)
            {
                selectedEmojis.Add(Emojis[random.Next(Emojis.Length)]);
            }
            emojis = string.Join("", selectedEmojis);

            result[id] = new MuppetNameInfo(name, emojis);
        }

        return result;
    }
}
