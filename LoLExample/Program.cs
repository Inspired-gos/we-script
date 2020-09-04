using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.Direct3D9;
using SharpDX.Mathematics;
using SharpDX.XInput;
using WeScriptWrapper;
using WeScript.SDK.UI;
using WeScript.SDK.UI.Components;
using WeScript.SDK.Utils;
using System.Runtime.InteropServices; //for StructLayout

namespace LoLExample
{
    class Program
    {

        [StructLayout(LayoutKind.Explicit)]
        public struct RendererStruct
        {
            [FieldOffset(0x6C)]
            public Matrix oView;
            [FieldOffset(0xAC)]
            public Matrix oProjection;
        }

        public enum spellSlot
        {
            _Q, _W, _E, _R, SUMMONER_1, SUMMONER_2, ITEM_1, ITEM_2, ITEM_3, ITEM_4, ITEM_5, ITEM_6, ITEM_7
        };

        [StructLayout(LayoutKind.Explicit)]
        public struct SpellDataStruct
        {
            [FieldOffset(0x20)]
            public UInt32 level;
            [FieldOffset(0x24)]
            public bool isLearned;
            [FieldOffset(0x28)]
            public float castTime;
            [FieldOffset(0x58)]
            public UInt32 ammo;
            [FieldOffset(0x64)]
            public float ammoTime;
            [FieldOffset(0x68)]
            public float ammoCd;
            [FieldOffset(0x70)]
            public UInt32 toggle;
            [FieldOffset(0x78)]
            public float spellCd;

            public float currentCd
            {
                get
                {
                    return (gameTime < this.castTime) ? (this.castTime - gameTime) : 0.0f;
                }
            }

            public float ammoCurrentCd
            {
                get
                {
                    return (gameTime < this.ammoTime) ? (this.ammoTime - gameTime) : 0.0f;
                }
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct GameObjectStruct
        {
            [FieldOffset(0x0)]
            public UInt32 baseOffs;
            [FieldOffset(0x20)]
            public UInt16 oObjIndex;
            [FieldOffset(0x4C)]
            public UInt16 oObjTeam;
            [FieldOffset(0xCC)]
            public UInt32 oObjNetworkID;
            [FieldOffset(0x1D8)]
            public Vector3 oObjPos;
            [FieldOffset(0x26C)]
            public byte oObjVisibility;
            [FieldOffset(0xDC4)]
            public float oObjHealth;
            [FieldOffset(0xDD4)]
            public float oObjMaxHealth;
            [FieldOffset(0x12AC)]
            public float oObjArmor;
            [FieldOffset(0x12C4)]
            public float oObjMoveSpeed;
            [FieldOffset(0x12CC)]
            public float oObjAtkRange;

            public string oObjChampionName
            {
                get
                {
                    return Memory.ReadString(processHandle, (IntPtr)(this.baseOffs + 0x312c), false);
                }
            }

            public SpellDataStruct GetSpellData(spellSlot splSlot)
            {
                var ptr = Memory.ReadPointer(processHandle, (IntPtr)(this.baseOffs + 0x2708 + 0x478 + (uint)splSlot * 4), isWow64Process);
                return SDKUtil.ReadStructure<SpellDataStruct>(processHandle, ptr);
            }

        }



        public static IntPtr processHandle = IntPtr.Zero; //processHandle variable used by OpenProcess (once)
        public static bool gameProcessExists = false; //avoid drawing if the game process is dead, or not existent
        public static bool isWow64Process = false; //we all know the game is 32bit, but anyway...
        public static bool isGameOnTop = false; //we should avoid drawing while the game is not set on top
        public static bool isOverlayOnTop = false; //we might allow drawing visuals, while the user is working with the "menu"
        public static uint PROCESS_ALL_ACCESS = 0x1FFFFF; //hardcoded access right to OpenProcess
        public static Vector2 wndMargins = new Vector2(0, 0); //if the game window is smaller than your desktop resolution, you should avoid drawing outside of it
        public static Vector2 wndSize = new Vector2(0, 0); //get the size of the game window ... to know where to draw


        public static IntPtr GameBase = IntPtr.Zero;
        public static IntPtr GameSize = IntPtr.Zero;
        public static float gameTime = 0;

        public static IntPtr oLocalPlayer = IntPtr.Zero;
        public static IntPtr oHeroManager = IntPtr.Zero;
        public static IntPtr oRenderer = IntPtr.Zero;
        public static IntPtr oGameTime = IntPtr.Zero;

        public static Menu RootMenu { get; private set; }
        public static Menu VisualsMenu { get; private set; }

        class Components
        {
            public static readonly MenuKeyBind MainAssemblyToggle = new MenuKeyBind("mainassemblytoggle", "Toggle the whole assembly effect by pressing key:", VirtualKeyCode.Delete, KeybindType.Toggle, true);
            public static class VisualsComponent
            {
                public static readonly MenuBool DrawRangeCircle = new MenuBool("rngcircle", "Draw Range Circle around Champions", true);
                public static readonly MenuColor RangeCircleColorAlly = new MenuColor("alliescirclecol", "Range Circle Allies Color", new SharpDX.Color(0, 255, 0, 100));
                public static readonly MenuColor RangeCircleColorNmy = new MenuColor("enemiescirclecol", "Range Circle Enemies Color", new SharpDX.Color(255, 0, 0, 100));
                public static readonly MenuBool DrawSpellTracker = new MenuBool("spelltrack", "Draw Spell Tracker for Champions", true);
            }
        }

        public static void InitializeMenu()
        {
            VisualsMenu = new Menu("visualsmenu", "Visuals Menu")
            {
                Components.VisualsComponent.DrawRangeCircle,
                Components.VisualsComponent.RangeCircleColorAlly,
                Components.VisualsComponent.RangeCircleColorNmy,
                Components.VisualsComponent.DrawSpellTracker,
            };


            RootMenu = new Menu("lolexample", "LoL Test Assembly - Awareness", true)
            {
                Components.MainAssemblyToggle.SetToolTip("The magical boolean which completely disables/enables the assembly!"),
                VisualsMenu,
            };
            RootMenu.Attach();
        }

        static void Main(string[] args)
        {
            Console.WriteLine("LoL Test Assembly - Spell awareness and range circles :)");
            Renderer.OnRenderer += OnRenderer;
            Memory.OnTick += OnTick;
            InitializeMenu();
        }

        private static void OnTick(int counter, EventArgs args)
        {
            if (processHandle == IntPtr.Zero) //if we still don't have a handle to the process
            {
                var wndHnd = Memory.FindWindowClassName("RiotWindowClass"); //classname
                if (wndHnd != IntPtr.Zero) //if it exists
                {
                    //Console.WriteLine("weheree");
                    var calcPid = Memory.GetPIDFromHWND(wndHnd); //get the PID of that same process
                    if (calcPid > 0) //if we got the PID
                    {
                        processHandle = Memory.OpenProcess(PROCESS_ALL_ACCESS, calcPid); //the driver will get a stripped handle, but doesn't matter, it's still OK
                        if (processHandle != IntPtr.Zero)
                        {
                            //if we got access to the game, check if it's x64 bit, this is needed when reading pointers, since their size is 4 for x86 and 8 for x64
                            isWow64Process = Memory.IsProcess64Bit(processHandle);
                        }
                        else
                        {
                            //Console.WriteLine("failed to get handle");
                        }
                    }
                }
            }
            else //else we have a handle, lets check if we should close it, or use it
            {
                var wndHnd = Memory.FindWindowClassName("RiotWindowClass"); //classname
                if (wndHnd != IntPtr.Zero) //window still exists, so handle should be valid? let's keep using it
                {
                    //the lines of code below execute every 33ms outside of the renderer thread, heavy code can be put here if it's not render dependant
                    gameProcessExists = true;
                    wndMargins = Renderer.GetWindowMargins(wndHnd);
                    wndSize = Renderer.GetWindowSize(wndHnd);
                    isGameOnTop = Renderer.IsGameOnTop(wndHnd);
                    isOverlayOnTop = Overlay.IsOnTop();

                    if (GameBase == IntPtr.Zero) //do we have access to Gamebase address?
                    {
                        GameBase = Memory.GetModule(processHandle, null, isWow64Process); //if not, find it
                        Console.WriteLine($"GameBase: {GameBase.ToString("X")}");
                    }
                    else
                    {
                        if (GameSize == IntPtr.Zero)
                        {
                            GameSize = Memory.GetModuleSize(processHandle, null, isWow64Process);
                            Console.WriteLine($"GameSize: {GameSize.ToString("X")}");
                        }
                        else
                        {
                            if (oLocalPlayer == IntPtr.Zero)
                            {
                                oLocalPlayer = (IntPtr)(GameBase.ToInt64() + 0x34E1A34); //A1 ? ? ? ? 85 C0 74 07 05 ? ? ? ? EB 02 33 C0 56
                                Console.WriteLine($"oLocalPlayer: {oLocalPlayer.ToString("X")}");
                            }
                            if (oHeroManager == IntPtr.Zero)
                            {
                                oHeroManager = (IntPtr)(GameBase.ToInt64() + 0x288E754); //8B 35 ? ? ? ? 0F 57 ED 57 8B FB
                                Console.WriteLine($"oObjManager: {oHeroManager.ToString("X")}");
                            }
                            if (oRenderer == IntPtr.Zero)
                            {
                                oRenderer = (IntPtr)(GameBase.ToInt64() + 0x3508E90); //8B 15 ? ? ? ? 83 EC 08 F3
                                Console.WriteLine($"oRenderer: {oRenderer.ToString("X")}");
                            }
                            if (oGameTime == IntPtr.Zero)
                            {
                                oGameTime = (IntPtr)(GameBase.ToInt64() + 0x34D9C1C); //D9 5C 24 14 F3 0F 10 4C 24 14 0F 57 C0
                                Console.WriteLine($"oGameTime: {oGameTime.ToString("X")}");
                            }
                        }
                    }
                }
                else //else most likely the process is dead, clean up
                {
                    Memory.CloseHandle(processHandle); //close the handle to avoid leaks
                    processHandle = IntPtr.Zero; //set it like this just in case for C# logic
                    gameProcessExists = false;
                    //clear your offsets, modules
                    GameBase = IntPtr.Zero;
                    GameSize = IntPtr.Zero;
                    oLocalPlayer = IntPtr.Zero;
                    oHeroManager = IntPtr.Zero;
                    oRenderer = IntPtr.Zero;
                    gameTime = 0;
                }
            }
        }

        public static int XposX = 90;
        public static int YposY = 0;


        private static void OnRenderer(int fps, EventArgs args)
        {
            if (!gameProcessExists) return; //process is dead, don't bother drawing
            if ((!isGameOnTop) && (!isOverlayOnTop)) return; //if game and overlay are not on top, don't draw
            if (!Components.MainAssemblyToggle.Enabled) return; //main menu boolean to toggle the cheat on or off

            if (oRenderer != IntPtr.Zero)
            {
                var rendBase = Memory.ReadPointer(processHandle, oRenderer, isWow64Process);
                if (rendBase != IntPtr.Zero)
                {
                    var matStruct = SDKUtil.ReadStructure<RendererStruct>(processHandle, rendBase);
                    var finalMatrix = matStruct.oView * matStruct.oProjection;
                    var localPlayer = Memory.ReadPointer(processHandle, oLocalPlayer, isWow64Process);
                    if (localPlayer != IntPtr.Zero)
                    {
                        gameTime = Memory.ReadFloat(processHandle, oGameTime);
                        var lPdata = SDKUtil.ReadStructureEx<GameObjectStruct>(processHandle, localPlayer, isWow64Process);
                        var heroManager = Memory.ReadPointer(processHandle, oHeroManager, isWow64Process);
                        if (heroManager != IntPtr.Zero)
                        {
                            for (uint i = 0; i <= 12; i++)
                            {
                                var heroPtr = Memory.ReadPointer(processHandle, (IntPtr)(heroManager.ToInt64() + i * 4), isWow64Process);
                                if (heroPtr != IntPtr.Zero)
                                {
                                    var heroData = SDKUtil.ReadStructureEx<GameObjectStruct>(processHandle, heroPtr, isWow64Process);

                                    if ((heroData.oObjVisibility == 1) && (heroData.oObjTeam == 100 || heroData.oObjTeam == 200) && (heroData.oObjHealth > 0.1) && (heroData.oObjHealth < 10000) && (heroData.oObjMaxHealth > 99) && (heroData.oObjArmor > 0) && (heroData.oObjArmor < 1000) && (heroData.oObjPos.Y != 0.0f) && (heroData.oObjPos.X != 0.0f) && (heroData.oObjPos.Z != 0.0f)) //ghetto validity check
                                    {
                                        var QData = heroData.GetSpellData(spellSlot._Q);
                                        var WData = heroData.GetSpellData(spellSlot._W);
                                        var EData = heroData.GetSpellData(spellSlot._E);
                                        var RData = heroData.GetSpellData(spellSlot._R);
                                        var DData = heroData.GetSpellData(spellSlot.SUMMONER_1);
                                        var FData = heroData.GetSpellData(spellSlot.SUMMONER_2);

                                        Vector2 pos2D;
                                        if (Renderer.WorldToScreen(heroData.oObjPos, out pos2D, finalMatrix, wndMargins, wndSize, W2SType.TypeOGL))
                                        {
                                            if (Components.VisualsComponent.DrawSpellTracker.Enabled)
                                            {
                                                Renderer.DrawFilledRect(pos2D.X - XposX - 5 - 1, pos2D.Y + YposY + 3 + 12 - 1, 118 + 2, 12 + 2, new Color(00, 00, 00, 0x7A)); //whole bar
                                                Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * 0, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 00, 00, 0xAA)); //spell bars

                                                Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * 1, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 00, 00, 0xAA));
                                                Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * 2, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 00, 00, 0xAA));
                                                Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * 3, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 00, 00, 0xAA));

                                                Renderer.DrawFilledRect(pos2D.X - XposX - 5 + 121 - 1, pos2D.Y + YposY + 3 + 12 - 1, 60 + 2, 12 + 2, new Color(00, 00, 0x5A, 0x7A)); //whole bar

                                                Renderer.DrawFilledRect(pos2D.X - XposX + 121, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 00, 00, 0xAA)); //spell bars D
                                                Renderer.DrawFilledRect(pos2D.X - XposX + 121 + 4 + 23, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 00, 00, 0xAA)); //spell bars F

                                                if (QData.level > 0)
                                                {
                                                    for (uint j = 1; j <= QData.level; j++)
                                                    {
                                                        Renderer.DrawRect(pos2D.X - XposX + 27 * (uint)spellSlot._Q + j * 5 - 1, pos2D.Y + YposY + 3 + 21, 1, 2, new Color(0xFF, 0xFF, 00, 0xFF));
                                                    }
                                                    if (QData.ammoCurrentCd > 0)
                                                    {
                                                        if (QData.ammo > 0)
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._Q, pos2D.Y + YposY + 3 + 16, 23 - ((QData.ammoCurrentCd / QData.ammoCd) * 23), 4, new Color(0xFF, 0x7F, 00, 0xFF));
                                                        }
                                                        else
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._Q, pos2D.Y + YposY + 3 + 16, 23 - ((QData.ammoCurrentCd / QData.ammoCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (QData.currentCd > 0)
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._Q, pos2D.Y + YposY + 3 + 16, 23 - ((QData.currentCd / QData.spellCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                        }
                                                        else
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._Q, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 0xFF, 00, 0xFF));
                                                        }
                                                    }
                                                }



                                                if (WData.level > 0)
                                                {
                                                    for (uint j = 1; j <= WData.level; j++)
                                                    {
                                                        Renderer.DrawRect(pos2D.X - XposX + 27 * (uint)spellSlot._W + j * 5 - 1, pos2D.Y + YposY + 3 + 21, 1, 2, new Color(0xFF, 0xFF, 00, 0xFF));
                                                    }
                                                    if (WData.ammoCurrentCd > 0)
                                                    {
                                                        if (WData.ammo > 0)
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._W, pos2D.Y + YposY + 3 + 16, 23 - ((WData.ammoCurrentCd / WData.ammoCd) * 23), 4, new Color(0xFF, 0x7F, 00, 0xFF));
                                                        }
                                                        else
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._W, pos2D.Y + YposY + 3 + 16, 23 - ((WData.ammoCurrentCd / WData.ammoCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (WData.currentCd > 0)
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._W, pos2D.Y + YposY + 3 + 16, 23 - ((WData.currentCd / WData.spellCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                        }
                                                        else
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._W, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 0xFF, 00, 0xFF));
                                                        }
                                                    }
                                                }



                                                if (EData.level > 0)
                                                {
                                                    for (uint j = 1; j <= EData.level; j++)
                                                    {
                                                        Renderer.DrawRect(pos2D.X - XposX + 27 * (uint)spellSlot._E + j * 5 - 1, pos2D.Y + YposY + 3 + 21, 1, 2, new Color(0xFF, 0xFF, 00, 0xFF));
                                                    }
                                                    if (EData.ammoCurrentCd > 0)
                                                    {
                                                        if (EData.ammo > 0)
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._E, pos2D.Y + YposY + 3 + 16, 23 - ((EData.ammoCurrentCd / EData.ammoCd) * 23), 4, new Color(0xFF, 0x7F, 00, 0xFF));
                                                        }
                                                        else
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._E, pos2D.Y + YposY + 3 + 16, 23 - ((EData.ammoCurrentCd / EData.ammoCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (EData.currentCd > 0)
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._E, pos2D.Y + YposY + 3 + 16, 23 - ((EData.currentCd / EData.spellCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                        }
                                                        else
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._E, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 0xFF, 00, 0xFF));
                                                        }
                                                    }
                                                }


                                                if (RData.level > 0)
                                                {
                                                    for (uint j = 1; j <= RData.level; j++)
                                                    {
                                                        Renderer.DrawRect(pos2D.X - XposX + 27 * (uint)spellSlot._R + j * 5 - 1, pos2D.Y + YposY + 3 + 21, 1, 2, new Color(0xFF, 0xFF, 00, 0xFF));
                                                    }
                                                    if (RData.ammoCurrentCd > 0)
                                                    {
                                                        if (RData.ammo > 0)
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._R, pos2D.Y + YposY + 3 + 16, 23 - ((RData.ammoCurrentCd / RData.ammoCd) * 23), 4, new Color(0xFF, 0x7F, 00, 0xFF));
                                                        }
                                                        else
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._R, pos2D.Y + YposY + 3 + 16, 23 - ((RData.ammoCurrentCd / RData.ammoCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (RData.currentCd > 0)
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._R, pos2D.Y + YposY + 3 + 16, 23 - ((RData.currentCd / RData.spellCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                        }
                                                        else
                                                        {
                                                            Renderer.DrawFilledRect(pos2D.X - XposX + 3 + 27 * (uint)spellSlot._R, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 0xFF, 00, 0xFF));
                                                        }
                                                    }
                                                }

                                                if (DData.currentCd > 0)
                                                {
                                                    Renderer.DrawFilledRect(pos2D.X - XposX + 121, pos2D.Y + YposY + 3 + 16, 23 - ((DData.currentCd / DData.spellCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                }
                                                else
                                                {
                                                    Renderer.DrawFilledRect(pos2D.X - XposX + 121, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 0xFF, 00, 0xFF));
                                                }


                                                if (FData.currentCd > 0)
                                                {
                                                    Renderer.DrawFilledRect(pos2D.X - XposX + 121 + 4 + 23, pos2D.Y + YposY + 3 + 16, 23 - ((FData.currentCd / FData.spellCd) * 23), 4, new Color(0xFF, 00, 00, 0xFF));
                                                }
                                                else
                                                {
                                                    Renderer.DrawFilledRect(pos2D.X - XposX + 121 + 4 + 23, pos2D.Y + YposY + 3 + 16, 23, 4, new Color(00, 0xFF, 00, 0xFF));
                                                }
                                            }

                                            if (Components.VisualsComponent.DrawRangeCircle.Enabled)
                                            {
                                                CircleRendering.Render(finalMatrix, (heroData.oObjTeam == lPdata.oObjTeam) ? Components.VisualsComponent.RangeCircleColorAlly.Color : Components.VisualsComponent.RangeCircleColorNmy.Color, heroData.oObjAtkRange + 55.0f, heroData.oObjPos);
                                            }

                                        }
                                    }

                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
