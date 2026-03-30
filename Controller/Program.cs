using System;
using System.IO;
using System.Windows.Forms;

namespace GnwayController
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // ── 全局异常捕获：崩溃时弹窗 + 写日志，绝不静默消失 ──
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (s, e) =>
                ShowCrash("UI线程异常", e.Exception);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                ShowCrash("未处理异常", (Exception)e.ExceptionObject);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ShowCrash("启动失败", ex);
            }
        }

        static void ShowCrash(string title, Exception ex)
        {
            // 写日志文件（exe 同目录）
            try
            {
                string logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                File.WriteAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}\n{ex}\n");
            }
            catch { /* 写日志本身失败则忽略 */ }

            MessageBox.Show(
                $"{title}：\n\n{ex.Message}\n\n详情已写入 crash.log",
                "GnwayAgent 启动错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            Environment.Exit(1);
        }
    }
}
