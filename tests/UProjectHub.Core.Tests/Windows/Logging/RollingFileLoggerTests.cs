using System.Text;
using UProjectHub.Core.Diagnostics;
using UProjectHub.Core.Tests.Time;
using UProjectHub.Windows.Logging;

namespace UProjectHub.Core.Tests.Windows.Logging;

[TestClass]
public sealed class RollingFileLoggerTests
{
    private static readonly DateTimeOffset TestTime = new(
        2026,
        8,
        27,
        14,
        30,
        0,
        TimeSpan.FromHours(9));

    [TestMethod]
    public void DefaultRetentionPolicyIsTwoMiBAndThreeBackups()
    {
        var policy = LogRetentionPolicy.Default;

        Assert.AreEqual(2 * 1024 * 1024, policy.MaxFileBytes);
        Assert.AreEqual(3, policy.MaxBackupFiles);
    }

    [TestMethod]
    public void RetentionPolicyRejectsNonPositiveValues()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LogRetentionPolicy(0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LogRetentionPolicy(1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LogRetentionPolicy(-1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LogRetentionPolicy(1, -1));
    }

    [TestMethod]
    public void FormatsInfoWarningAndErrorWithUtcTimestamp()
    {
        using var fixture = TemporaryLogDirectory.Create();
        var logger = fixture.CreateLogger(TestTime);

        logger.Info("Application started.");
        logger.Warning("Project refresh issue.");
        logger.Error("Launch failed.");

        Assert.IsTrue(File.Exists(fixture.LogFilePath));
        CollectionAssert.AreEqual(
            new[]
            {
                "2026-08-27T05:30:00.0000000+00:00 [INFO] Application started.",
                "2026-08-27T05:30:00.0000000+00:00 [WARN] Project refresh issue.",
                "2026-08-27T05:30:00.0000000+00:00 [ERROR] Launch failed.",
            },
            File.ReadAllLines(fixture.LogFilePath));
    }

    [TestMethod]
    public void ExceptionAndMultilineTextStayInOneNormalizedRecord()
    {
        using var fixture = TemporaryLogDirectory.Create();
        var logger = fixture.CreateLogger(TestTime);

        logger.Error(
            "first\r\nsecond",
            new InvalidOperationException("bad\rvalue\nnext"));

        var lines = File.ReadAllLines(fixture.LogFilePath);
        Assert.HasCount(1, lines);
        Assert.AreEqual(
            "2026-08-27T05:30:00.0000000+00:00 [ERROR] first second | InvalidOperationException: bad value next",
            lines[0]);
        Assert.DoesNotContain("System.InvalidOperationException", lines[0]);
        Assert.DoesNotContain(" at ", lines[0]);
    }

    [TestMethod]
    public void WritesBomlessUtf8AndRoundTripsKoreanText()
    {
        using var fixture = TemporaryLogDirectory.Create();
        var logger = fixture.CreateLogger(TestTime);

        logger.Info("프로젝트 이름: 별빛 게임");

        var bytes = File.ReadAllBytes(fixture.LogFilePath);
        Assert.IsFalse(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.Contains("프로젝트 이름: 별빛 게임", text);
    }

    [TestMethod]
    public void EntriesAppendWithoutRotationBelowThreshold()
    {
        using var fixture = TemporaryLogDirectory.Create();
        var logger = fixture.CreateLogger(
            TestTime,
            new LogRetentionPolicy(1024, 2));

        logger.Info("first record");
        logger.Info("second record");

        Assert.HasCount(2, File.ReadAllLines(fixture.LogFilePath));
        Assert.IsFalse(File.Exists(fixture.GetBackupPath(1)));
    }

    [TestMethod]
    public void NextEntryRotatesBeforeThresholdWouldBeExceeded()
    {
        using var fixture = TemporaryLogDirectory.Create();
        var logger = fixture.CreateLogger(
            TestTime,
            new LogRetentionPolicy(100, 2));

        logger.Info($"first-{new string('1', 30)}");
        logger.Info($"second-{new string('2', 30)}");

        var active = File.ReadAllText(fixture.LogFilePath);
        var firstBackup = File.ReadAllText(fixture.GetBackupPath(1));
        Assert.Contains("second-", active);
        Assert.DoesNotContain("first-", active);
        Assert.Contains("first-", firstBackup);
        Assert.IsLessThanOrEqualTo(100L, new FileInfo(fixture.LogFilePath).Length);
        Assert.IsLessThanOrEqualTo(100L, new FileInfo(fixture.GetBackupPath(1)).Length);
    }

    [TestMethod]
    public void RotationKeepsNewestBackupsAndPreservesUnrelatedFiles()
    {
        using var fixture = TemporaryLogDirectory.Create();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.LogFilePath)!);
        var unrelatedPath = Path.Combine(
            Path.GetDirectoryName(fixture.LogFilePath)!,
            "notes.txt");
        File.WriteAllText(unrelatedPath, "keep me");
        var logger = fixture.CreateLogger(
            TestTime,
            new LogRetentionPolicy(96, 2));

        logger.Info($"record-one-{new string('1', 30)}");
        logger.Info($"record-two-{new string('2', 30)}");
        logger.Info($"record-three-{new string('3', 30)}");
        logger.Info($"record-four-{new string('4', 30)}");

        Assert.Contains("record-four-", File.ReadAllText(fixture.LogFilePath));
        Assert.Contains("record-three-", File.ReadAllText(fixture.GetBackupPath(1)));
        Assert.Contains("record-two-", File.ReadAllText(fixture.GetBackupPath(2)));
        Assert.IsFalse(File.Exists(fixture.GetBackupPath(3)));
        Assert.AreEqual("keep me", File.ReadAllText(unrelatedPath));
    }

    [TestMethod]
    public void OversizedEntryIsRuneSafeAndMarkedAsTruncated()
    {
        using var fixture = TemporaryLogDirectory.Create();
        const int maxBytes = 96;
        var logger = fixture.CreateLogger(
            TestTime,
            new LogRetentionPolicy(maxBytes, 1));

        logger.Info(string.Concat(Enumerable.Repeat("한글🌟", 100)));

        var bytes = File.ReadAllBytes(fixture.LogFilePath);
        Assert.IsLessThanOrEqualTo(maxBytes, bytes.Length);
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.EndsWith("...[truncated]", text);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(5)]
    [DataRow(10)]
    public void ExtremelySmallPositiveLimitAlwaysProducesValidBoundedUtf8(
        int maxBytes)
    {
        using var fixture = TemporaryLogDirectory.Create();
        var logger = fixture.CreateLogger(
            TestTime,
            new LogRetentionPolicy(maxBytes, 1));

        logger.Error(
            string.Concat(Enumerable.Repeat("한글🌟", 20)),
            new InvalidOperationException("실패🌟"));

        var bytes = File.ReadAllBytes(fixture.LogFilePath);
        Assert.IsLessThanOrEqualTo(maxBytes, bytes.Length);
        _ = new UTF8Encoding(false, true).GetString(bytes);
    }

    [TestMethod]
    public async Task ConcurrentCallsProduceCompleteDistinctRecordsAsync()
    {
        using var fixture = TemporaryLogDirectory.Create();
        var logger = fixture.CreateLogger(
            TestTime,
            new LogRetentionPolicy(1024 * 1024, 2));
        const int workerCount = 8;
        const int recordsPerWorker = 25;

        var tasks = Enumerable.Range(0, workerCount)
            .Select(worker => Task.Run(() =>
            {
                for (var record = 0; record < recordsPerWorker; record++)
                {
                    logger.Info($"worker-{worker}-record-{record}");
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var lines = File.ReadAllLines(fixture.LogFilePath);
        Assert.HasCount(workerCount * recordsPerWorker, lines);
        Assert.AreEqual(lines.Length, lines.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(lines.All(line =>
            line.StartsWith(
                "2026-08-27T05:30:00.0000000+00:00 [INFO] worker-",
                StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ExpectedFilesystemFailureDoesNotEscapeLoggingCalls()
    {
        using var fixture = TemporaryLogDirectory.Create();
        Directory.CreateDirectory(fixture.LogFilePath);
        var logger = fixture.CreateLogger(TestTime);

        logger.Info("info");
        logger.Warning("warning");
        logger.Error("error");
        logger.Error("error", new IOException("write failed"));

        Assert.IsTrue(Directory.Exists(fixture.LogFilePath));
    }

    [TestMethod]
    public void NullLoggerAcceptsAllCallsWithoutSideEffects()
    {
        IAppLogger logger = new NullAppLogger();

        logger.Info("info");
        logger.Warning("warning");
        logger.Error("error");
        logger.Error("error", new InvalidOperationException("failure"));
    }

    private sealed class TemporaryLogDirectory : IDisposable
    {
        private TemporaryLogDirectory(string rootPath)
        {
            RootPath = rootPath;
            LogFilePath = Path.Combine(rootPath, "logs", "app.log");
        }

        public string RootPath { get; }

        public string LogFilePath { get; }

        public static TemporaryLogDirectory Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "UProjectHub.Tests",
                "RollingFileLogger",
                Guid.NewGuid().ToString("N"));
            return new TemporaryLogDirectory(rootPath);
        }

        public RollingFileLogger CreateLogger(
            DateTimeOffset utcNow,
            LogRetentionPolicy? policy = null) =>
            new(
                LogFilePath,
                policy ?? LogRetentionPolicy.Default,
                new FakeClock(utcNow));

        public string GetBackupPath(int index) => $"{LogFilePath}.{index}";

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
