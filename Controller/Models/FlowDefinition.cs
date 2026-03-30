// ============================================================
//  FlowDefinition.cs — 流程 JSON 数据模型
//  对应 flows/xxx.json 的结构
// ============================================================

using System.Collections.Generic;

namespace GnwayController.Models
{
    /// <summary>完整流程定义（对应 json 根节点）</summary>
    public class FlowDefinition
    {
        public string Name       { get; set; } = "";
        public int    MaxRounds  { get; set; } = 200;
        public List<FlowState> States { get; set; } = new List<FlowState>();
    }

    /// <summary>状态节点</summary>
    public class FlowState
    {
        public string  Id         { get; set; } = "";
        public string  Label      { get; set; } = "";
        public bool    StartState { get; set; } = false;

        /// <summary>进入此状态后需等待的前置条件</summary>
        public Condition? WaitFor    { get; set; }

        /// <summary>等待超时秒数，0=不超时</summary>
        public int    TimeoutSec  { get; set; } = 0;

        /// <summary>超时后跳转的状态ID（如 "ERROR" / "exit_detail"）</summary>
        public string OnTimeout   { get; set; } = "ERROR";

        public List<Transition> Transitions { get; set; } = new List<Transition>();
    }

    /// <summary>条件→动作→跳转 三元组</summary>
    public class Transition
    {
        public Condition          Condition { get; set; } = new Condition();
        public List<FlowAction>   Actions   { get; set; } = new List<FlowAction>();
        public string             Next      { get; set; } = "";
    }

    /// <summary>
    /// 条件描述。Type 取值：
    ///   always / windowExists / windowNotExists /
    ///   controlExists / controlNotExists / controlEnabled /
    ///   anyPopup / popupBodyContains
    /// </summary>
    public class Condition
    {
        public string Type    { get; set; } = "always";
        public string Window  { get; set; } = "";
        public string Control { get; set; } = "";
        /// <summary>popupBodyContains 时的匹配文本</summary>
        public string Text    { get; set; } = "";
    }

    /// <summary>
    /// 动作描述。支持三种互斥用法：
    ///   Cmd 非空  → 发送到 Agent 的命令字符串（或 "popupclick|confirm" 虚拟命令）
    ///   Sleep > 0 → 等待指定毫秒
    /// Fallback 在 Cmd 失败时作为备用命令重试
    /// </summary>
    public class FlowAction
    {
        public string? Cmd      { get; set; }
        public string? Fallback { get; set; }
        public int     Sleep    { get; set; } = 0;
    }
}
