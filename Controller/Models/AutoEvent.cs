// ============================================================
//  AutoEvent.cs — 单个录制事件的数据模型
//  一个 AutoEvent = 一个自动化步骤
//  包含：执行前的控件树快照 + 要执行的动作
// ============================================================

using System;
using System.Collections.Generic;

namespace GnwayController.Models
{
    /// <summary>单个录制的自动化步骤</summary>
    public class AutoEvent
    {
        /// <summary>唯一 ID，如 "evt_20260330_143022"</summary>
        public string Id { get; set; } = "";

        /// <summary>用户取的名称，如 "选待处理行"</summary>
        public string Name { get; set; } = "";

        /// <summary>操作目标窗口标题（模糊匹配 Contains）</summary>
        public string WindowName { get; set; } = "";

        /// <summary>执行前的控件树快照（用于等待/验证当前状态）</summary>
        public ControlSnapshot Snapshot { get; set; } = new ControlSnapshot();

        /// <summary>要执行的动作</summary>
        public EventAction Action { get; set; } = new EventAction();
    }

    /// <summary>控件信息（快照中的每一项）</summary>
    public class ControlInfo
    {
        public string Type    { get; set; } = "";   // Button, TextBox, DataGrid...
        public string Name    { get; set; } = "";   // 控件名
        public bool   Enabled { get; set; } = true; // 是否可用
    }

    /// <summary>控件树快照</summary>
    public class ControlSnapshot
    {
        public List<ControlInfo> Controls    { get; set; } = new List<ControlInfo>();
        public string            CapturedAt  { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>
    /// 动作描述。Type 取值：
    ///   click      - 点击按钮/控件
    ///   input      - 向输入框键入文字（Value = 要输入的文字）
    ///   select     - 下拉框选择选项（Value = 选项文字）
    ///   gridnext   - 在表格中自动找下一条包含 MatchText 的行并选中
    ///                （ColIndex = 检查哪一列；MatchText = 如 "待处理"）
    ///                若找不到匹配行 → 流程算作全部完成
    ///   sleep      - 等待 SleepMs 毫秒
    ///   popupclick - 在弹窗（WindowName）里点击按钮（ControlName）
    /// </summary>
    public class EventAction
    {
        public string Type        { get; set; } = "click";
        public string ControlName { get; set; } = "";  // 目标控件名
        public string Value       { get; set; } = "";  // input/select 时的值
        public string MatchText   { get; set; } = "";  // gridnext 时的匹配文字
        public int    ColIndex    { get; set; } = 0;   // gridnext 时检查第几列
        public int    SleepMs     { get; set; } = 0;   // sleep 时的毫秒数

        /// <summary>给 UI 显示的可读描述</summary>
        public string Describe()
        {
            return Type switch
            {
                "click"      => $"点击  [{ControlName}]",
                "input"      => $"输入  [{ControlName}] = \"{Value}\"",
                "select"     => $"选择  [{ControlName}] = \"{Value}\"",
                "gridnext"   => $"选行  [{ControlName}] 含\"{MatchText}\" (列{ColIndex})",
                "sleep"      => $"等待  {SleepMs} ms",
                "popupclick" => $"弹窗  点击[{ControlName}]",
                _            => Type
            };
        }
    }
}
