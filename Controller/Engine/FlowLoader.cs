// ============================================================
//  FlowLoader.cs — 从 JSON 文件加载流程定义
//  使用 .NET 4.8 内置 JavaScriptSerializer，无需第三方 DLL
// ============================================================

using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using GnwayController.Models;

namespace GnwayController.Engine
{
    public static class FlowLoader
    {
        /// <summary>加载 flows/ 目录下的 JSON 文件，返回 FlowDefinition</summary>
        public static FlowDefinition Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"流程文件不存在: {jsonPath}");

            string json = File.ReadAllText(jsonPath, Encoding.UTF8);

            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue
            };

            // JavaScriptSerializer 对属性名大小写不敏感，camelCase JSON → PascalCase C# 自动匹配
            var def = serializer.Deserialize<FlowDefinition>(json)
                      ?? throw new InvalidDataException($"JSON 解析失败: {jsonPath}");

            if (def.States == null || def.States.Count == 0)
                throw new InvalidDataException("流程文件中没有 states 节点，请检查 JSON 格式");

            // 确保各集合不为 null（JavaScriptSerializer 对空数组可能返回 null）
            foreach (var st in def.States)
            {
                st.Transitions ??= new System.Collections.Generic.List<Transition>();
                foreach (var tr in st.Transitions)
                    tr.Actions ??= new System.Collections.Generic.List<FlowAction>();
            }

            return def;
        }

        /// <summary>列出 flowsDir 下所有 *.json 文件（不含路径，不含扩展名）</summary>
        public static string[] ListFlowNames(string flowsDir)
        {
            if (!Directory.Exists(flowsDir)) return Array.Empty<string>();
            var files = Directory.GetFiles(flowsDir, "*.json");
            var names = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                names[i] = Path.GetFileNameWithoutExtension(files[i]);
            return names;
        }
    }
}
