using System;
using Celeste;
using Celeste.Mod;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxMenu {
    private static EaseInSubMenu subMenuItem;

    public static EaseInSubMenu CreateSubMenu(TextMenu menu, bool inGame) {
        subMenuItem = new EaseInSubMenu("Show Hitboxes".ToDialogText(), false).Apply(subMenu => {
            subMenu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), TasSettings.ShowHitboxes).Change(value => TasSettings.ShowHitboxes = value));
            subMenu.Add(new TextMenu.OnOff("Show Trigger Hitboxes".ToDialogText(), TasSettings.ShowTriggerHitboxes).Change(value =>
                TasSettings.ShowTriggerHitboxes = value));
            subMenu.Add(new TextMenu.OnOff("Show Unloaded Rooms Hitboxes".ToDialogText(), TasSettings.ShowUnloadedRoomsHitboxes).Change(value =>
                TasSettings.ShowUnloadedRoomsHitboxes = value));
            subMenu.Add(new TextMenu.OnOff("Show Camera Hitboxes".ToDialogText(), TasSettings.ShowCameraHitboxes).Change(value =>
                TasSettings.ShowCameraHitboxes = value));
            subMenu.Add(new TextMenu.Option<ActualCollideHitboxType>("Actual Collide Hitboxes".ToDialogText()).Apply(option => {
                Array enumValues = Enum.GetValues(typeof(ActualCollideHitboxType));
                foreach (ActualCollideHitboxType value in enumValues) {
                    option.Add(value.ToString().SpacedPascalCase().ToDialogText(), value, value.Equals(TasSettings.ShowActualCollideHitboxes));
                }

                option.Change(value => TasSettings.ShowActualCollideHitboxes = value);
            }));
            subMenu.Add(new TextMenu.OnOff("Simplified Hitboxes".ToDialogText(), TasSettings.SimplifiedHitboxes).Change(value =>
                TasSettings.SimplifiedHitboxes = value));
            subMenu.Add(new TextMenuExt.IntSlider("Un-collidable Hitboxes Opacity".ToDialogText(), 0, 10, TasSettings.UnCollidableHitboxesOpacity)
                .Change(value =>
                    TasSettings.UnCollidableHitboxesOpacity = value));
            subMenu.Add(HitboxColor.CreateEntityHitboxColorButton(menu, inGame));
            subMenu.Add(HitboxColor.CreateTriggerHitboxColorButton(menu, inGame));
            subMenu.Add(HitboxColor.CreatePlatformHitboxColorButton(menu, inGame));
        });
        return subMenuItem;
    }

    public static void AddSubMenuDescription(TextMenu menu, bool inGame) {
        if (inGame) {
            subMenuItem.AddDescription(menu, "Hitbox Color Description 2".ToDialogText());
            subMenuItem.AddDescription(menu, "Hitbox Color Description 1".ToDialogText());
        }
    }
}