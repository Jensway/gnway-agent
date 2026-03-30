// ============================================================
//  FlowLoader.cs — 从 JSON 文件加载流程定义
// ============================================================

using System.IO;
using GnwayController.Models;
using Newtonsoft.Json;

namespace GnwayController.Engine
{
    public static class FlowLoader
    {
        /// <summary>
        /// 加载 flows/ 目录下的 JSON 文件，返回 FlowDefinition。
        /// jsonPath 为绝对路径。
        /// </summary>
        public static FlowDefinition Load(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);

            var settings = new JsonSerializerSettings
            {
                // 允许 JSON 中使用 camelCase 属性名匹配 C# PascalCase 属性
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            var def = JsonConvert.DeserializeObject<FlowDefinition>(json, settings)
                      ?? throw new InvalidDataException($"JSON 解析失败: {jsonPath}");

            if (def.States.Count == 0)
                throw new InvalidDataException("流程定义中没有 states 节点");

            return def;
        }

        /// <summary>扫描 flowsDir 目录内所有 *.json 文件，返回文件名列表</summary>
        public static string[] ListFlowFiles(string flowsDir)
        {
            if (!Directory.Exists(flowsDir)) return System.Array.Empty<string>();
            return Directory.GetFiles(flowsDir, "*.json");
        }
    }
}
