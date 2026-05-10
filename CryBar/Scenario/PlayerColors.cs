namespace CryBar.Scenario;

public readonly record struct PlayerColor(float R, float G, float B);

public static class PlayerColors
{
    // Indexed 0..12. Player 0 = Gaia (gray); 1..12 follow the AoM:R reference roster.
    static readonly PlayerColor[] Table =
    {
        new(0.55f, 0.55f, 0.55f), // 0 Gaia (gray)
        new(0.13f, 0.25f, 1.00f), // 1 Blue
        new(1.00f, 0.06f, 0.06f), // 2 Red
        new(1.00f, 0.93f, 0.00f), // 3 Yellow
        new(0.13f, 0.94f, 0.13f), // 4 Green
        new(0.00f, 0.88f, 0.88f), // 5 Cyan
        new(0.94f, 0.25f, 0.88f), // 6 Magenta
        new(1.00f, 0.53f, 0.00f), // 7 Orange
        new(0.52f, 0.13f, 0.25f), // 8 Maroon
        new(0.13f, 0.63f, 0.47f), // 9 Teal-Green
        new(1.00f, 0.63f, 0.50f), // 10 Salmon
        new(0.50f, 0.13f, 0.94f), // 11 Purple
        new(1.00f, 1.00f, 1.00f), // 12 White
    };

    public static PlayerColor GetRgb(byte playerId) =>
        playerId < Table.Length ? Table[playerId] : Table[Table.Length - 1];

    public static int Count => Table.Length;
}
