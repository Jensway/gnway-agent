// ============================================================
//  Program.cs — 应用入口
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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
