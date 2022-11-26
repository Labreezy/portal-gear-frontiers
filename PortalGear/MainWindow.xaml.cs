using System;
using System.ComponentModel;
using System.Windows;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Reloaded.Assembler;
using Reloaded.Memory;
using Reloaded.Memory.Sources;

namespace PortalGear
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
       
        public MainWindow()
        {
            InitializeComponent();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Vec3
        {
            public float x;
            public float y;
            public float z;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Quaternion
        {
            public float x;
            public float y;
            public float z;
            public float w;
        }
        private Process frontiersProc;
        private int cave1_off = 0x56808;
        private int cave2_off = 0x821E5;
        private int pos_inject_off = 0x79cad1;
        private int cam_inject_off = 0xB96391;
        private IntPtr cave1_loc;
        private IntPtr cave2_loc;
        private IntPtr pos_inject_loc;
        private IntPtr cam_inject_loc;
        private nuint cam_jmp_loc;
        private nuint pos_jmp_loc;
        private nuint addr_info_loc;
        private ulong curr_pos_addr;
        private ulong curr_cam_addr;
        private byte[] origbytes_pos_speed = new byte[15];
        private byte[] origbytes_camera = new byte[10];
        private Assembler asmblr = new Assembler();
        private ExternalMemory front_mem;
        private bool isAttached = false;
        private Vec3 saved_pos = new Vec3();
        private Vec3 saved_speed = new Vec3();
        private Quaternion saved_rotation = new Quaternion();
        private Vec3 saved_camera = new Vec3();
        private DispatcherTimer posUpdateTimer;
        private void posUpdate_Tick(object sender, EventArgs e)
        {
            if (!isAttached)
            {
                return;
            }

            
            float[] this_pos = new float[3];
            front_mem.Read<ulong>(addr_info_loc, out curr_pos_addr);
            if (curr_pos_addr != 0)
            {
                
                front_mem.Read<ExternalMemory, float>((nuint)(curr_pos_addr+0x80), out this_pos, 3, false);
                string block_text = $"X: {this_pos[0]:F2}\nY: {this_pos[1]:F2}\nZ: {this_pos[2]}";
                posTextBlock.Text = block_text;
            }
        }
        
        private void AttachBtn_OnClick(object sender, RoutedEventArgs e)
        {
            Process proc = Process.GetProcessesByName("SonicFrontiers").FirstOrDefault();
            if (proc == null)
            {
                MessageBox.Show("Sonic Frontiers Not Found");
                return;
            }

            if (!isAttached)
            {
                posUpdateTimer = new DispatcherTimer();
                frontiersProc = proc;
                front_mem = new ExternalMemory(frontiersProc);
                cave1_loc = IntPtr.Add(frontiersProc.MainModule.BaseAddress, cave1_off);
                cave2_loc = IntPtr.Add(frontiersProc.MainModule.BaseAddress, cave2_off);
                pos_inject_loc = IntPtr.Add(frontiersProc.MainModule.BaseAddress, pos_inject_off);
                cam_inject_loc = IntPtr.Add(frontiersProc.MainModule.BaseAddress, cam_inject_off);
                front_mem.SafeRead<ExternalMemory, byte>((nuint) pos_inject_loc.ToInt64(), out origbytes_pos_speed, 15,
                    false);
                front_mem.SafeRead<ExternalMemory, byte>((nuint) cam_inject_loc.ToInt64(), out origbytes_camera, 10);
                pos_jmp_loc = front_mem.Allocate(128);
                cam_jmp_loc = front_mem.Allocate(128);
                addr_info_loc = front_mem.Allocate(128);
                
                byte[] pos_jmpbytes =
                    asmblr.Assemble($@"use64
mov rsi,0x{(ulong) pos_jmp_loc:x}
jmp rsi
xor rsi, rsi
inc rsi");

                byte[] pos_hook =
                    asmblr.Assemble(
                        $@"use64
mov rsi,0x{(ulong) addr_info_loc:X}
mov [rsi],rbx
movaps xmm6,xmm7
mulps xmm6,[rsp+0x20]
addps xmm6,[rbx+0x80]
mov rsi,0x{(pos_inject_loc.ToInt64() + 9):x}
jmp rsi");
                byte[] cam_jmpbytes = asmblr.Assemble($@"use64  

push r10
mov r10,0x{(ulong)cam_jmp_loc:x}
jmp r10
pop r10
nop
nop");
                byte[] cam_hook = asmblr.Assemble($@"use64
mov r10,0x{(ulong) (addr_info_loc + 8):X}
mov [r10],rax
mov rcx,rax
mov rbx,rax
movups [rax],xmm0
movups xmm1,[rdi+0x10]
mov r10,0x{(ulong) (pos_inject_loc.ToInt64() + 10):x}
jmp r10
"); //+10 is 9 for mov+jmp + an extra 1 for push
                front_mem.Write<ExternalMemory, byte>(pos_jmp_loc, pos_hook);
                front_mem.SafeWrite<ExternalMemory, byte>((nuint) pos_inject_loc.ToInt64(), pos_jmpbytes);
                isAttached = true;
                attachBtn.Content = "Detach";
                posUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, 200);
                posUpdateTimer.Tick += new EventHandler(posUpdate_Tick);
                posUpdateTimer.Start();
            }
            else
            {
                posUpdateTimer.Stop();
                front_mem.SafeWrite<ExternalMemory, Byte>((nuint) pos_inject_loc.ToInt64(), origbytes_pos_speed, false);
                isAttached = false;
                attachBtn.Content = "Attach";
            }
        }
        
        private void saveBtn_OnClick(object sender, RoutedEventArgs e)
        {
            float[] this_pos = new float[3];
            front_mem.Read<ulong>(addr_info_loc, out curr_pos_addr);
            front_mem.Read<ulong>((addr_info_loc + 8), out curr_cam_addr);
            if (curr_pos_addr != 0)
            {
                front_mem.Read<Vec3>((nuint) (curr_pos_addr + 0x80), out saved_pos, true);
                front_mem.Read<Quaternion>((nuint) (curr_pos_addr + 0x90), out saved_rotation, true);
                front_mem.Read<Vec3>((nuint) (curr_pos_addr + 0xD0), out saved_speed, true);
                string block_text = $"X: {saved_pos.x:F2}\nY: {saved_pos.y:F2}\nZ: {saved_pos.z:F2}";
                savedPosTextBlock.Text = block_text;
            }

            if (curr_cam_addr != 0)
            {
                front_mem.Read<Vec3>((nuint) (curr_cam_addr + 0x30), out saved_camera, true);
                camTextBlock.Text = $"(Camera) X:{saved_camera.x:F2}\nY: {saved_camera.y:F2}\nZ: {saved_pos.z:F2}";
            }
        }

        private void loadBtn_OnClick(object sender, RoutedEventArgs e)
        {
            front_mem.Read<ulong>(addr_info_loc, out curr_pos_addr);
            front_mem.Read<ulong>((addr_info_loc + 8), out curr_cam_addr);
            if (curr_pos_addr != 0)
            {
                front_mem.Write<Vec3>((nuint)(curr_pos_addr + 0x80), ref saved_pos, true);
                front_mem.Write<Quaternion>((nuint)(curr_pos_addr + 0x90), ref saved_rotation, true);
                front_mem.Write<Vec3>((nuint)(curr_pos_addr + 0xD0), ref saved_speed, true);
            }

            if (curr_cam_addr != 0)
            {
                front_mem.Write<Vec3>((nuint) (curr_cam_addr + 0x30), ref saved_camera, true);
            }
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (isAttached)
            {
                posUpdateTimer.Stop();
                front_mem.SafeWrite<ExternalMemory, byte>((nuint) pos_inject_loc.ToInt64(), origbytes_pos_speed, false);
                front_mem.SafeWrite<ExternalMemory, byte>((nuint) cam_inject_loc.ToInt64(), origbytes_camera, false);
            }
        }
    }
}