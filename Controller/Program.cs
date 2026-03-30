// ============================================================
//  Program.cs — 应用入口，带全局异常捕获
//  出错时弹 MessageBox，不再静默消失
// ============================================================

using System;
using System.Windows.Forms;

namespace GnwayController
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // 捕获所有未处理异常，弹窗显示而不是静默退出
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (s, e) =>
                MessageBox.Show(
                    $"运行时错误：\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                    "GnwayAgent 错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show(
                    $"严重错误：\n{((Exception)e.ExceptionObject)}",
                    "GnwayAgent 严重错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
