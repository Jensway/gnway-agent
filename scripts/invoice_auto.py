#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
invoice_auto.py — 发票自动化处理脚本
=====================================
流程：
  1. 在状态列中选中"未处理"行（按 ↓ 键导航）
  2. 点击"生成"按钮，等待完成
  3. 发票详情窗口弹出 → 点"保存"→ 等保存完成
  4. 点"审核" → 等"审核成功"弹窗 → 点"确定"
  5. 等待可能出现的"勾稽成功"弹窗（不一定有）→ 点"确定"
  6. 点"退出"，回到列表
  7. 检查是否还有"未处理"，有则循环，无则结束

用法：
  python invoice_auto.py <服务器IP> [程序窗口名]

示例：
  python invoice_auto.py 192.168.1.105
  python invoice_auto.py 192.168.1.105 "发票管理"
"""

import subprocess
import sys
import time
import os

# ── 配置区（可根据实际情况修改） ─────────────────────────────
CONTROLLER = os.path.join(os.path.dirname(__file__), "..", "controller.exe")
SERVER_IP   = "."            # 默认本机，运行时从命令行参数覆盖
WIN_LIST    = "发票管理"     # 状态列表所在的窗口标题（模糊匹配）
WIN_DETAIL  = "发票详情"     # 发票详情窗口标题（模糊匹配）

# 超时配置（秒）
TIMEOUT_GENERATE = 60        # 等待"生成"操作完成的最长时间
TIMEOUT_SAVE     = 30        # 等待"保存"完成的最长时间
TIMEOUT_AUDIT    = 30        # 等待"审核成功"弹窗的最长时间
TIMEOUT_JIUJI    = 10        # 等待"勾稽成功"弹窗的最长时间（可能不出现）
MAX_ROUNDS       = 200       # 最多循环次数，防止死循环
# ─────────────────────────────────────────────────────────────


def ctrl(command: str) -> str:
    """调用 controller.exe 发送一条命令，返回结果字符串"""
    cmd = [CONTROLLER, SERVER_IP, command]
    try:
        result = subprocess.run(
            cmd, capture_output=True, text=True, encoding="utf-8", timeout=15
        )
        output = (result.stdout or "").strip()
        if result.returncode != 0 and not output:
            output = f"ERR:{(result.stderr or '').strip()}"
        return output
    except subprocess.TimeoutExpired:
        return "ERR:controller 调用超时"
    except FileNotFoundError:
        print(f"[致命] 找不到 controller.exe: {CONTROLLER}")
        print("  请先编译 Controller 项目，或把 controller.exe 放到 scripts 同目录")
        sys.exit(1)


def ok(result: str) -> bool:
    return result.startswith("OK")


def log(msg: str):
    ts = time.strftime("%H:%M:%S")
    print(f"[{ts}] {msg}")


def wait_window(title: str, timeout: int) -> bool:
    """轮询等待某个窗口出现，成功返回 True"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        r = ctrl(f"exists|{title}|{title}")   # 用窗口自身标题检测存在
        # exists 返回 OK:true / OK:false
        if r == "OK:true":
            return True
        time.sleep(1)
    return False


def wait_control_exists(window: str, control: str, timeout: int) -> bool:
    """轮询等待某个控件出现"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        r = ctrl(f"exists|{window}|{control}")
        if r == "OK:true":
            return True
        time.sleep(1)
    return False


def dismiss_popup(title: str, timeout: int) -> bool:
    """
    等待弹窗出现并点击"确定"。
    timeout 内出现则返回 True，未出现返回 False（可选弹窗用 False 不报错）
    """
    log(f"  等待弹窗「{title}」（最多 {timeout}s）...")
    r = ctrl(f"wait|{title}|{title}|confirm|{timeout}")
    if ok(r):
        log(f"  ✓ 弹窗「{title}」已点确定")
        return True
    log(f"  ○ 弹窗「{title}」未出现（跳过）")
    return False


def has_unprocessed() -> bool:
    """检查列表窗口中是否还存在"未处理"状态的条目"""
    r = ctrl(f"exists|{WIN_LIST}|未处理")
    return r == "OK:true"


def select_first_unprocessed() -> bool:
    """
    在列表中选中第一条"未处理"的行。
    尝试直接点击"未处理"条目；若失败则用 ↓ 键导航。
    """
    log("  尝试点击「未处理」行...")
    r = ctrl(f"click|{WIN_LIST}|未处理")
    if ok(r):
        log("  ✓ 已选中「未处理」")
        return True

    # 备用：模拟按 ↓ 键（有些列表控件只能键盘导航）
    log("  直接点击失败，改用 ↓ 键导航...")
    # 先把焦点给列表控件
    ctrl(f"focus|{WIN_LIST}|状态")   # 状态列所在的列表控件名，按实际调整
    time.sleep(0.3)
    # 发送 ↓ 键（通过 input 发 SendKeys）
    r = ctrl(f"input|{WIN_LIST}|{WIN_LIST}|{{DOWN}}")   # 备用方案
    return True   # 先假定成功，后续 "生成" 按钮是否可用会验证


# ════════════════════════════════════════════════════════════════
#  主流程
# ════════════════════════════════════════════════════════════════
def main():
    global SERVER_IP, WIN_LIST, WIN_DETAIL

    # 解析命令行参数
    if len(sys.argv) >= 2:
        SERVER_IP = sys.argv[1]
    if len(sys.argv) >= 3:
        WIN_LIST = sys.argv[2]

    print("=" * 55)
    print("  发票自动化处理脚本")
    print(f"  Agent 服务器: {SERVER_IP}")
    print(f"  列表窗口:     {WIN_LIST}")
    print(f"  详情窗口:     {WIN_DETAIL}")
    print("=" * 55)

    # 验证 agent 连通性
    log("检查 Agent 连通性...")
    r = ctrl("windows")
    if r.startswith("ERR"):
        print(f"[错误] 无法连接 Agent：{r}")
        print("  请确认 agent.exe 已在服务器上运行，且 IP 正确")
        sys.exit(1)
    log("✓ Agent 连接正常")
    print()

    # ── 主循环 ──────────────────────────────────────────────────
    for round_no in range(1, MAX_ROUNDS + 1):

        # 检查是否还有未处理
        log(f"【第 {round_no} 轮】检查是否有「未处理」条目...")
        if not has_unprocessed():
            log("✅ 全部已生成，任务完成！")
            break

        # ① 选中第一条"未处理"
        log("① 选中「未处理」行")
        if not select_first_unprocessed():
            log("  ✗ 无法选中未处理行，停止")
            sys.exit(1)
        time.sleep(0.5)

        # ② 点击"生成"按钮
        log("② 点击「生成」按钮")
        r = ctrl(f"click|{WIN_LIST}|生成")
        if not ok(r):
            log(f"  ✗ 点击失败: {r}")
            sys.exit(1)

        # ③ 等待"发票详情"窗口弹出
        log(f"③ 等待「{WIN_DETAIL}」窗口（最多 {TIMEOUT_GENERATE}s）...")
        if not wait_window(WIN_DETAIL, TIMEOUT_GENERATE):
            log(f"  ✗ 超时：「{WIN_DETAIL}」窗口未出现")
            sys.exit(1)
        log(f"  ✓ 「{WIN_DETAIL}」已出现")
        time.sleep(0.5)

        # ④ 点击工具栏"保存"
        log("④ 点击「保存」")
        r = ctrl(f"click|{WIN_DETAIL}|保存")
        if not ok(r):
            log(f"  ✗ 保存失败: {r}")
            sys.exit(1)

        # ⑤ 等待"审核"按钮可用（保存完成后才会激活）
        log(f"⑤ 等待「审核」按钮可用（最多 {TIMEOUT_SAVE}s）...")
        if not wait_control_exists(WIN_DETAIL, "审核", TIMEOUT_SAVE):
            log("  ✗ 超时：「审核」按钮未出现，可能保存失败")
            sys.exit(1)
        log("  ✓ 「审核」可用")
        time.sleep(0.3)

        # ⑥ 点击"审核"
        log("⑥ 点击「审核」")
        r = ctrl(f"click|{WIN_DETAIL}|审核")
        if not ok(r):
            log(f"  ✗ 审核点击失败: {r}")
            sys.exit(1)

        # ⑦ 等待"审核成功"弹窗并点确定
        log(f"⑦ 等待「审核成功」弹窗（最多 {TIMEOUT_AUDIT}s）...")
        if not dismiss_popup("审核成功", TIMEOUT_AUDIT):
            log("  ✗ 「审核成功」弹窗未出现，可能审核失败，停止")
            sys.exit(1)
        time.sleep(0.5)

        # ⑧ 等待可能出现的"勾稽成功"弹窗（可选）
        log("⑧ 检查「勾稽成功」弹窗（可能不出现）...")
        dismiss_popup("勾稽成功", TIMEOUT_JIUJI)   # 不出现也没关系
        time.sleep(0.3)

        # ⑨ 点击"退出"，回到列表
        log("⑨ 点击「退出」")
        r = ctrl(f"click|{WIN_DETAIL}|退出")
        if not ok(r):
            # 有时叫"关闭"
            r = ctrl(f"click|{WIN_DETAIL}|关闭")
        log(f"  {'✓' if ok(r) else '△'} {r}")

        # 等列表窗口恢复
        time.sleep(1)
        print()

    else:
        log(f"⚠ 已达最大循环次数 {MAX_ROUNDS}，强制停止")

    print()
    print("=" * 55)
    print("  脚本结束")
    print("=" * 55)


if __name__ == "__main__":
    main()
