// ============================================================
//  EngineEvent.cs — FlowEngine 向 UI 推送的事件模型
// ============================================================

namespace GnwayController.Models
{
    public enum EngineEventType
    {
        Log,              // 普通日志行
        StateChanged,     // 状态切换
        RoundChanged,     // 进入新一轮
        Paused,           // 引擎已暂停
        Resumed,          // 引擎已继续
        Completed,        // 全部完成
        Error,            // 出现错误
        NeedIntervention  // 需要人工干预（自动暂停）
    }

    public enum LogLevel
    {
        Info,   // 白色
        Ok,     // 绿色
        Wait,   // 黄色
        Popup,  // 青色
        Warn,   // 橙色
        Error,  // 红色
        Debug   // 灰色
    }

    public class EngineEvent
    {
        public EngineEventType Type    { get; set; }
        public string          Message { get; set; } = "";
        public LogLevel        Level   { get; set; } = LogLevel.Info;
        public string?         StateId { get; set; }
        public int             Round   { get; set; }
    }
}
