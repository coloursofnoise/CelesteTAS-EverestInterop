using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Utils;
using GameInput = Celeste.Input;

namespace TAS;

public static class Manager {
    private static readonly ConcurrentQueue<Action> mainThreadActions = new();

    private static readonly GetDelegate<SummitVignette, bool> SummitVignetteReady = FastReflection.CreateGetDelegate<SummitVignette, bool>("ready");
    private static readonly DUpdateVirtualInputs UpdateVirtualInputs;

    public static bool Running;
    public static readonly InputController Controller = new();
    public static States LastStates, States, NextStates;
    public static float FrameLoops { get; private set; } = 1f;
    public static bool UltraFastForwarding => FrameLoops >= 100 && Running;
    public static bool SlowForwarding => FrameLoops < 1f;

    private static bool SkipSlowForwardingFrame =>
        FrameLoops < 1f && (int) ((Engine.FrameCounter + 1) * FrameLoops) == (int) (Engine.FrameCounter * FrameLoops);

    public static bool SkipFrame => States.HasFlag(States.FrameStep) || SkipSlowForwardingFrame;

    static Manager() {
        MethodInfo updateVirtualInputs = typeof(MInput).GetMethodInfo("UpdateVirtualInputs");
        UpdateVirtualInputs = (DUpdateVirtualInputs) updateVirtualInputs.CreateDelegate(typeof(DUpdateVirtualInputs));

        AttributeUtils.CollectMethods<EnableRunAttribute>();
        AttributeUtils.CollectMethods<DisableRunAttribute>();
    }

    private static bool ShouldForceState =>
        NextStates.HasFlag(States.FrameStep) && !Hotkeys.FastForward.OverrideCheck && !Hotkeys.SlowForward.OverrideCheck;

    public static void AddMainThreadAction(Action action) {
        if (Thread.CurrentThread == MainThreadHelper.MainThread) {
            action();
        } else {
            mainThreadActions.Enqueue(action);
        }
    }

    private static void ExecuteMainThreadActions() {
        while (mainThreadActions.TryDequeue(out Action action)) {
            action.Invoke();
        }
    }

    public static void Update() {
        LastStates = States;
        ExecuteMainThreadActions();
        Hotkeys.Update();
        Savestates.HandleSaveStates();
        HandleFrameRates();
        CheckToEnable();
        FrameStepping();

        if (States.HasFlag(States.Enable)) {
            Running = true;

            if (!SkipFrame) {
                Controller.AdvanceFrame(out bool canPlayback);

                // stop TAS if breakpoint is not placed at the end
                if (Controller.Break && Controller.CanPlayback) {
                    Controller.NextCommentFastForward = null;
                    NextStates |= States.FrameStep;
                    FrameLoops = 1;
                }

                if (!canPlayback) {
                    DisableRun();
                } else if (SafeCommand.DisallowUnsafeInput && Controller.CurrentFrameInTas > 1) {
                    if (Engine.Scene is not (Level or LevelLoader or LevelExit)) {
                        DisableRun();
                    } else if (Engine.Scene is Level level && level.Tracker.GetEntity<TextMenu>() is { } menu) {
                        if (menu.Items.FirstOrDefault() is TextMenu.Header header && header.Title == Dialog.Clean("options_title") ||
                            menu.Items.FirstOrDefault() is TextMenuExt.HeaderImage {Image: "menu/everest"}) {
                            DisableRun();
                        }
                    }
                }
            }
        } else {
            Running = false;
            if (!Engine.Instance.IsActive) {
                // MInput.Keyboard.UpdateNull();
                MInput.Keyboard.PreviousState = MInput.Keyboard.CurrentState;
                MInput.Keyboard.CurrentState = default;

                // MInput.Mouse.UpdateNull();
                MInput.Mouse.PreviousState = MInput.Mouse.CurrentState;
                MInput.Mouse.CurrentState = default;

                for (int i = 0; i < 4; i++) {
                    if (MInput.Active) {
                        MInput.GamePads[i].Update();
                    } else {
                        MInput.GamePads[i].UpdateNull();
                    }
                }

                UpdateVirtualInputs();
            }
        }

        SendStateToStudio();
    }

    private static void HandleFrameRates() {
        FrameLoops = 1;

        if (States.HasFlag(States.Enable) && !States.HasFlag(States.FrameStep) && !NextStates.HasFlag(States.FrameStep)) {
            if (Controller.HasFastForward) {
                FrameLoops = Controller.FastForwardSpeed;
            }

            if (Hotkeys.FastForward.Check) {
                FrameLoops = TasSettings.FastForwardSpeed;
            } else if (Hotkeys.SlowForward.Check) {
                FrameLoops = TasSettings.SlowForwardSpeed;
            } else if (Math.Round(Hotkeys.RightThumbSticksX * TasSettings.FastForwardSpeed) is var fastForwardSpeed and >= 2) {
                FrameLoops = (int) fastForwardSpeed;
            } else if (Hotkeys.RightThumbSticksX < 0f &&
                       (1 + Hotkeys.RightThumbSticksX) * TasSettings.SlowForwardSpeed is var slowForwardSpeed and <= 0.9f) {
                FrameLoops = Math.Max(slowForwardSpeed, FastForward.MinSpeed);
            }
        }
    }

    private static void FrameStepping() {
        bool frameAdvance = Hotkeys.FrameAdvance.Check && !Hotkeys.StartStop.Check;
        bool pause = Hotkeys.PauseResume.Check && !Hotkeys.StartStop.Check;

        if (States.HasFlag(States.Enable)) {
            if (NextStates.HasFlag(States.FrameStep)) {
                States |= States.FrameStep;
                NextStates &= ~States.FrameStep;
            }

            if (frameAdvance && !Hotkeys.FrameAdvance.LastCheck) {
                if (!States.HasFlag(States.FrameStep)) {
                    States |= States.FrameStep;
                    NextStates &= ~States.FrameStep;
                } else {
                    States &= ~States.FrameStep;
                    NextStates |= States.FrameStep;
                }
            } else if (pause && !Hotkeys.PauseResume.LastCheck) {
                if (!States.HasFlag(States.FrameStep)) {
                    States |= States.FrameStep;
                    NextStates &= ~States.FrameStep;
                } else {
                    States &= ~States.FrameStep;
                    NextStates &= ~States.FrameStep;
                }
            } else if (LastStates.HasFlag(States.FrameStep) && States.HasFlag(States.FrameStep) &&
                       (Hotkeys.FastForward.Check || Hotkeys.SlowForward.Check && Engine.FrameCounter % 10 == 0) &&
                       !Hotkeys.FastForwardComment.Check) {
                States &= ~States.FrameStep;
                NextStates |= States.FrameStep;
            }
        }
    }

    private static void CheckToEnable() {
        if (!Savestates.SpeedrunToolInstalled && Hotkeys.Restart.Released) {
            DisableRun();
            EnableRun();
            return;
        }

        if (Hotkeys.StartStop.Check) {
            if (States.HasFlag(States.Enable)) {
                NextStates |= States.Disable;
            } else {
                NextStates |= States.Enable;
            }
        } else if (NextStates.HasFlag(States.Enable)) {
            EnableRun();
        } else if (NextStates.HasFlag(States.Disable)) {
            DisableRun();
        }
    }

    public static void EnableRun() {
        if (Engine.Scene is GameLoader) {
            return;
        }

        Running = true;
        States |= States.Enable;
        States &= ~States.FrameStep;
        NextStates &= ~States.Enable;
        AttributeUtils.Invoke<EnableRunAttribute>();
        Controller.RefreshInputs(true);
    }

    public static void DisableRun() {
        Running = false;

        LastStates = States.None;
        States = States.None;
        NextStates = States.None;

        // fix the input that was last held stays for a frame when it ends
        if (MInput.GamePads != null && MInput.GamePads.FirstOrDefault(data => data.Attached) is { } gamePadData) {
            gamePadData.CurrentState = new GamePadState();
        }

        AttributeUtils.Invoke<DisableRunAttribute>();
        Controller.Stop();
    }

    public static void DisableRunLater() {
        NextStates |= States.Disable;
    }

    public static void SendStateToStudio() {
        if (UltraFastForwarding && Engine.FrameCounter % 23 > 0) {
            return;
        }

        StudioInfo studioInfo = new(
            Controller.Previous?.Line ?? -1,
            $"{Controller.CurrentFrameInInput}{Controller.Previous?.RepeatString ?? ""}",
            Controller.CurrentFrameInTas,
            Controller.Inputs.Count,
            Savestates.StudioHighlightLine,
            (int) States,
            GameInfo.StudioInfo,
            GameInfo.LevelName,
            GameInfo.ChapterTime
        );
        StudioCommunicationClient.Instance?.SendState(studioInfo, !ShouldForceState);
    }

    public static bool IsLoading() {
        switch (Engine.Scene) {
            case Level level:
                return level.IsAutoSaving() && level.Session.Level == "end-cinematic";
            case SummitVignette summit:
                return !SummitVignetteReady(summit);
            case Overworld overworld:
                return overworld.Current is OuiFileSelect {SlotIndex: >= 0} slot && slot.Slots[slot.SlotIndex].StartingGame;
            default:
                bool isLoading = Engine.Scene is LevelExit or LevelLoader or GameLoader || Engine.Scene.GetType().Name == "LevelExitToLobby";
                return isLoading;
        }
    }

    public static void SetInputs(InputFrame input) {
        GamePadDPad pad = default;
        GamePadThumbSticks sticks = default;
        GamePadState gamePadState = default;
        if (input.HasActions(Actions.Feather)) {
            SetFeather(input, ref pad, ref sticks);
        } else {
            SetDPad(input, ref pad, ref sticks);
        }

        SetGamePadState(input, ref gamePadState, ref pad, ref sticks);

        MInput.GamePadData gamePadData = MInput.GamePads[GameInput.Gamepad];
        gamePadData.PreviousState = gamePadData.CurrentState;
        gamePadData.CurrentState = gamePadState;

        MInput.Keyboard.PreviousState = MInput.Keyboard.CurrentState;
        if (input.HasActions(Actions.Confirm)) {
            MInput.Keyboard.CurrentState = new KeyboardState(BindingHelper.Confirm2);
        } else {
            MInput.Keyboard.CurrentState = new KeyboardState();
        }

        UpdateVirtualInputs();
    }

    private static void SetFeather(InputFrame input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
        pad = new GamePadDPad(ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        sticks = new GamePadThumbSticks(input.AngleVector2, new Vector2(0, 0));
    }

    private static void SetDPad(InputFrame input, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
        pad = new GamePadDPad(
            input.HasActions(Actions.Up) ? ButtonState.Pressed : ButtonState.Released,
            input.HasActions(Actions.Down) ? ButtonState.Pressed : ButtonState.Released,
            input.HasActions(Actions.Left) ? ButtonState.Pressed : ButtonState.Released,
            input.HasActions(Actions.Right) ? ButtonState.Pressed : ButtonState.Released
        );
        sticks = new GamePadThumbSticks(new Vector2(0, 0), new Vector2(0, 0));
    }

    private static void SetGamePadState(InputFrame input, ref GamePadState state, ref GamePadDPad pad, ref GamePadThumbSticks sticks) {
        state = new GamePadState(
            sticks,
            new GamePadTriggers(input.HasActions(Actions.Journal) ? 1f : 0f, 0),
            new GamePadButtons(
                (input.HasActions(Actions.Jump) ? BindingHelper.JumpAndConfirm : 0)
                | (input.HasActions(Actions.Jump2) ? BindingHelper.Jump2 : 0)
                | (input.HasActions(Actions.DemoDash) ? BindingHelper.DemoDash : 0)
                | (input.HasActions(Actions.DemoDash2) ? BindingHelper.DemoDash2 : 0)
                | (input.HasActions(Actions.Dash) ? BindingHelper.DashAndTalkAndCancel : 0)
                | (input.HasActions(Actions.Dash2) ? BindingHelper.Dash2AndCancel : 0)
                | (input.HasActions(Actions.Grab) ? BindingHelper.Grab : 0)
                | (input.HasActions(Actions.Start) ? BindingHelper.Pause : 0)
                | (input.HasActions(Actions.Restart) ? BindingHelper.QuickRestart : 0)
                | (input.HasActions(Actions.Up) ? BindingHelper.Up : 0)
                | (input.HasActions(Actions.Down) ? BindingHelper.Down : 0)
                | (input.HasActions(Actions.Left) ? BindingHelper.Left : 0)
                | (input.HasActions(Actions.Right) ? BindingHelper.Right : 0)
                | (input.HasActions(Actions.Journal) ? BindingHelper.JournalAndTalk : 0)
            ),
            pad
        );
    }


    //The things we do for faster replay times
    private delegate void DUpdateVirtualInputs();
}

[AttributeUsage(AttributeTargets.Method)]
internal class EnableRunAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class DisableRunAttribute : Attribute { }