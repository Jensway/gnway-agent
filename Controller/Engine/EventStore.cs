// ============================================================
//  EventStore.cs — 事件与流程的持久化层
//  · events/{id}.json  ← 每个录制事件
//  · flow.json         ← 有序的步骤 ID 列表
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using GnwayController.Models;

namespace GnwayController.Engine
{
    public class EventStore
    {
        private readonly string               _eventsDir;
        private readonly string               _flowFile;
        private readonly JavaScriptSerializer _json;

        public string EventsDir => _eventsDir;

        public EventStore(string baseDir)
        {
            _eventsDir = Path.Combine(baseDir, "events");
            _flowFile  = Path.Combine(baseDir, "flow.json");
            _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            try { Directory.CreateDirectory(_eventsDir); } catch { }
        }

        // ── 事件 CRUD ─────────────────────────────────────────

        public void Save(AutoEvent evt)
        {
            string path = Path.Combine(_eventsDir, $"{evt.Id}.json");
            File.WriteAllText(path, _json.Serialize(evt), Encoding.UTF8);
        }

        public List<AutoEvent> LoadAll()
        {
            var list = new List<AutoEvent>();
            if (!Directory.Exists(_eventsDir)) return list;

            foreach (string file in Directory.GetFiles(_eventsDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file, Encoding.UTF8);
                    var evt = _json.Deserialize<AutoEvent>(json);
                    if (evt != null && !string.IsNullOrEmpty(evt.Id))
                        list.Add(evt);
                }
                catch { /* 损坏文件跳过 */ }
            }
            return list;
        }

        public void Delete(string id)
        {
            string path = Path.Combine(_eventsDir, $"{id}.json");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // ── 流程（有序步骤 ID 列表）──────────────────────────

        /// <summary>将有序 ID 列表保存为 flow.json</summary>
        public void SaveFlow(List<string> orderedIds)
        {
            File.WriteAllText(_flowFile, _json.Serialize(orderedIds), Encoding.UTF8);
        }

        /// <summary>读取 flow.json；若不存在返回 null</summary>
        public List<string>? LoadFlow()
        {
            if (!File.Exists(_flowFile)) return null;
            try
            {
                string json = File.ReadAllText(_flowFile, Encoding.UTF8);
                return _json.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch { return null; }
        }

        // ── 工具 ─────────────────────────────────────────────

        /// <summary>生成唯一 ID（基于时间戳）</summary>
        public static string NewId()
            => "evt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

        /// <summary>解析 listcontrols 返回的控件列表</summary>
        public static List<ControlInfo> ParseControlList(string rawOk)
        {
            // rawOk = "OK:Type|Depth|MagicId|Text|Rect|Enabled\n..."
            string body = rawOk.StartsWith("OK:") ? rawOk.Substring(3) : rawOk;
            var list = new List<ControlInfo>();
            foreach (var line in body.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var p = trimmed.Split('|');
                if (p.Length < 6)
                {
                    // Fallback to old format if necessary
                    if (p.Length >= 2)
                    {
                        list.Add(new ControlInfo
                        {
                            Type    = p[0],
                            Name    = p[1],
                            Enabled = p.Length > 2 && p[2] == "1"
                        });
                    }
                    continue;
                }
                
                string magicId = p[2];
                string text = p[3];
                // Build a display name if we still want a combined one for backward compatibility
                string combinedName = string.IsNullOrWhiteSpace(text) ? magicId : $"{magicId} {text}";

                list.Add(new ControlInfo
                {
                    Type    = p[0],
                    Depth   = int.TryParse(p[1], out int d) ? d : 0,
                    MagicId = magicId,
                    Text    = text,
                    Rect    = p[4],
                    Enabled = p[5] == "1",
                    Name    = combinedName
                });
            }
            return list;
        }
    }
}
