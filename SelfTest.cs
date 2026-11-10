namespace DownTidy;

public static class SelfTest
{
    public static int Run()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "DownTidy-SelfTest", Guid.NewGuid().ToString("N"));
        var download = Path.Combine(testRoot, "Downloads");
        var data = Path.Combine(testRoot, "Data");
        Directory.CreateDirectory(download);

        try
        {
            var rules = new RuleService(data, download);
            using var organizer = new OrganizerService(rules, download, data);

            File.WriteAllText(Path.Combine(download, "report.pdf"), "pdf");
            File.WriteAllText(Path.Combine(download, "photo.jpg"), "jpg");
            File.WriteAllText(Path.Combine(download, "unknown.xyz"), "unknown");
            File.WriteAllText(Path.Combine(download, "partial.crdownload"), "partial");

            var first = organizer.OrganizeNowAsync().GetAwaiter().GetResult();
            Assert(first.Moved == 2 && first.Skipped == 2 && first.Failed == 0, "默认规则与未知/临时文件跳过");
            Assert(File.Exists(Path.Combine(download, "文档", "report.pdf")), "PDF 自动归入文档目录");
            Assert(File.Exists(Path.Combine(download, "图片", "photo.jpg")), "图片自动归入图片目录");
            Assert(File.Exists(Path.Combine(download, "unknown.xyz")), "未知类型保持原位");
            Assert(File.Exists(Path.Combine(download, "partial.crdownload")), "下载中文件保持原位");
            Pass("手动整理、默认分类、未知类型和临时文件保护");

            File.WriteAllText(Path.Combine(download, "report.pdf"), "new-pdf");
            var second = organizer.OrganizeNowAsync().GetAwaiter().GetResult();
            Assert(second.Moved == 1, "第二次整理成功");
            Assert(File.Exists(Path.Combine(download, "文档", "report (1).pdf")), "同名文件自动改名");
            Assert(File.ReadAllText(Path.Combine(download, "文档", "report.pdf")) == "pdf", "已有目标文件未被覆盖");
            Pass("同名冲突自动改名且不覆盖");

            var undo = organizer.UndoLastAsync().GetAwaiter().GetResult();
            Assert(undo.Restored == 1 && undo.Failed == 0, "撤销最近一次整理");
            Assert(File.Exists(Path.Combine(download, "report.pdf")), "撤销后文件回到原位置");
            Assert(!File.Exists(Path.Combine(download, "文档", "report (1).pdf")), "撤销后目标文件已移回");
            Pass("撤销最近一次整理");

            var before = rules.CurrentJson;
            const string invalid = "{\"rules\":[{\"name\":\"坏规则\",\"extensions\":[\".exe\"],\"targetFolder\":\"..\\\\escape\"}]}";
            var rejected = false;
            try { rules.Save(invalid); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "越界路径被拒绝");
            Assert(rules.CurrentJson == before, "无效规则没有覆盖旧规则");
            Pass("无效规则与路径越界保护");

            organizer.Start();
            File.WriteAllText(Path.Combine(download, "auto.txt"), "auto");
            Assert(WaitFor(() => File.Exists(Path.Combine(download, "文档", "auto.txt")), 15000), "监控到稳定文件后自动归位");
            Pass("自动监控、稳定检测与自动归位");

            var partial = Path.Combine(download, "archive.crdownload");
            File.WriteAllText(partial, "zip");
            Thread.Sleep(6500);
            Assert(File.Exists(partial), "自动监控不处理 .crdownload");
            File.Move(partial, Path.Combine(download, "archive.zip"));
            Assert(WaitFor(() => File.Exists(Path.Combine(download, "压缩包", "archive.zip")), 15000), "下载完成后重命名触发自动归位");
            Pass("下载临时文件到完成文件的全过程");

            Console.WriteLine("ALL SELF-TESTS PASSED");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SELF-TEST FAILED: " + ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(testRoot, true); } catch { }
        }
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(300);
        }
        return condition();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Pass(string name) => Console.WriteLine("[PASS] " + name);
}
