namespace RepViewer.Core;

/// <summary>Stable entry points shared by the desktop UI, exporters and Shell extension.</summary>
public static class ReplayApi
{
    public static ReplayDocument Read(byte[] buffer) => ReplayParser.Parse(buffer);
    public static ReplayDocument ReadFile(string path) => ReplayParser.ParseFile(path);
    public static byte[] DecodeForDiagnostics(byte[] buffer) => ReplayParser.DecodeForAnalysis(buffer);

    public static ReplayStatistics AnalyzeStage(byte[] buffer, int stageIndex)
    {
        var replay = Read(buffer);
        if ((uint)stageIndex >= replay.Stages.Count) throw new ArgumentOutOfRangeException(nameof(stageIndex));
        return replay.Stages[stageIndex].Statistics;
    }

    public static ReplayStatistics AnalyzeAll(byte[] buffer)
    {
        var replay = Read(buffer);
        var matrix = ReplayStatistics.CreateMatrix();
        var frames = ReplayStatistics.CreateFrameMatrix();
        var frameBase = 0;
        foreach (var stage in replay.Stages)
        {
            var statistics = stage.Statistics;
            for (var from = 0; from < 16; from++)
                for (var to = 0; to < 16; to++)
                {
                    matrix[from][to] += statistics.Matrix[from][to];
                    frames[from][to].AddRange(statistics.Frames[from][to].Select(frame => frameBase + frame));
                }
            frameBase += stage.Keys.Count;
        }
        return new ReplayStatistics { Matrix = matrix, Frames = frames };
    }
}
