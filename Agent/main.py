import os
import sys
import time
import socket
import win32pipe
import win32file
import pywintypes
from automation import process_command

PIPE_NAME = r'\\.\pipe\GnwayAgentPipe'

def print_local_ips():
    print("本机 IP 地址（Controller 连接时使用）:")
    try:
        hostnames = socket.gethostbyname_ex(socket.gethostname())
        for ip in hostnames[2]:
            # 跳过本地回环
            if ip.startswith("127."):
                continue
            print(f"  [Network] {ip}")
    except Exception as e:
        print("  （未检测到局域网网卡，请确认网络连接）")

def main():
    print("=== GnwayAgent 服务端 (Python 重构版) ===")
    print(f"进程ID: {os.getpid()}")
    print(f"管道名称: GnwayAgentPipe")
    print(f"主机名称: {socket.gethostname()}")
    print_local_ips()
    print("等待指令中... (Ctrl+C 退出)\n")

    while True:
        pipe = None
        try:
            # 创建命名管道
            pipe = win32pipe.CreateNamedPipe(
                PIPE_NAME,
                win32pipe.PIPE_ACCESS_DUPLEX,
                # 使用字节模式而非消息模式，以接受 C# 客户端的多次 Flush 流数据
                win32pipe.PIPE_TYPE_BYTE | win32pipe.PIPE_READMODE_BYTE | win32pipe.PIPE_WAIT,
                1,              # 实例数
                65536,          # 输出缓冲区
                65536,          # 输入缓冲区
                0,              # 默认超时
                None            # 默认安全属性
            )

            print("[等待] 客户端连接中...")
            win32pipe.ConnectNamedPipe(pipe, None)
            print("[连接] 客户端已连接")

            try:
                # 循环读取直到遇到换行符，防范 C# 的 StreamWriter.AutoFlush 将 BOM 和实际字符串分多次发来
                chunks = []
                while True:
                    hr, chunk = win32file.ReadFile(pipe, 4096)
                    if not chunk:
                        break
                    chunks.append(chunk)
                    if b'\n' in chunk:
                        break
                
                data = b''.join(chunks)
                if data:
                    cmd_line = data.decode('utf-8-sig').strip()
                    if not cmd_line:
                        win32file.WriteFile(pipe, "ERR:空命令\r\n".encode('utf-8'))
                        print("[跳过] 收到空字符或独立 BOM 测试连接")
                        continue

                    print(f"[收到] {cmd_line}")
                    
                    # 转交 automation.py 处理，同时传入 pipe 句柄用于流式返回
                    result = process_command(cmd_line, pipe)

                    if result is not None:
                        # 确保带有回车换行，方便 C# 的 StreamReader.ReadLine()
                        win32file.WriteFile(pipe, (result + "\r\n").encode('utf-8'))
                        # 为防止 print 内容过多，截断显示
                        print(f"[返回] {result[:100]}{'...' if len(result)>100 else ''}\n")
                    else:
                        print(f"[返回] <流式输出已处理>\n")

            except pywintypes.error as e:
                # ERROR_BROKEN_PIPE (109)
                if e.winerror != 109:
                    print(f"[管道错误] {e.args}")
            except Exception as e:
                win32file.WriteFile(pipe, f"ERR:服务端执行异常: {str(e)}\r\n".encode('utf-8'))
                print(f"[执行异常] {e}")

        except Exception as ex:
            print(f"[外层错误] {ex}")
            time.sleep(1)
        finally:
            # 释放连接
            if pipe:
                try:
                    win32file.FlushFileBuffers(pipe)
                    win32pipe.DisconnectNamedPipe(pipe)
                    win32file.CloseHandle(pipe)
                except:
                    pass

if __name__ == "__main__":
    main()
