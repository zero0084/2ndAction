package com.zero0084.omm;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.InstallSourceInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;

import com.google.android.play.core.appupdate.AppUpdateInfo;
import com.google.android.play.core.appupdate.AppUpdateManager;
import com.google.android.play.core.appupdate.AppUpdateManagerFactory;
import com.google.android.play.core.install.model.AppUpdateType;
import com.google.android.play.core.install.model.UpdateAvailability;
import com.google.android.gms.tasks.OnFailureListener;
import com.google.android.gms.tasks.OnSuccessListener;
import com.unity3d.player.UnityPlayer;

// 更新の確認(2026-11-14): Google Play の「アプリ内アップデート」(com.google.android.play:app-update)で、
// その人が Play から実際に入手できる新しい版があるかだけを確かめる。
//  ・Play から入れていない(開発の APK を直接入れた/他のストア)時は確かめない("notplay|<入れたアプリ>")。
//    → 未配信の版への更新を求めない。クローズドテスト・段階配信は Play 側が「その人に配信済みの版」だけを返す
//  ・結果は UnitySendMessage(go, method, "available|<versionCode>|<経過日数>" / "none|<状態>" / "notplay|<installer>" / "error|<理由>")
//  ・更新は強制しない。「更新する」はストアのこのアプリのページを開くだけ(Play の画面で更新する)
public class UpdateCheckBridge {
    public static final String PlayStore = "com.android.vending";

    public static String installer(Activity act) {
        try {
            String pkg = act.getPackageName();
            PackageManager pm = act.getPackageManager();
            if (Build.VERSION.SDK_INT >= 30) {
                InstallSourceInfo info = pm.getInstallSourceInfo(pkg);
                String s = info.getInstallingPackageName();
                return s == null ? "" : s;
            }
            @SuppressWarnings("deprecation") String s = pm.getInstallerPackageName(pkg);
            return s == null ? "" : s;
        } catch (Throwable e) {
            return "";
        }
    }

    public static void check(final Activity act, final String go, final String method) {
        final String inst = installer(act);
        if (!PlayStore.equals(inst)) { send(go, method, "notplay|" + inst); return; }
        try {
            AppUpdateManager m = AppUpdateManagerFactory.create(act.getApplicationContext());
            m.getAppUpdateInfo()
                .addOnSuccessListener(new OnSuccessListener<AppUpdateInfo>() {
                    public void onSuccess(AppUpdateInfo info) {
                        int a = info.updateAvailability();
                        if (a == UpdateAvailability.UPDATE_AVAILABLE && (info.isUpdateTypeAllowed(AppUpdateType.FLEXIBLE) || info.isUpdateTypeAllowed(AppUpdateType.IMMEDIATE))) {
                            Integer stale = info.clientVersionStalenessDays();
                            send(go, method, "available|" + info.availableVersionCode() + "|" + (stale == null ? -1 : stale));
                        } else send(go, method, "none|" + a);
                    }
                })
                .addOnFailureListener(new OnFailureListener() {
                    public void onFailure(Exception e) { send(go, method, "error|" + e.getClass().getSimpleName()); }
                });
        } catch (Throwable e) {
            send(go, method, "error|" + e.getClass().getSimpleName());
        }
    }

    // ストアのこのアプリのページ(Play があれば Play のアプリ、無ければブラウザの Play のページ)
    public static boolean openStorePage(Activity act) {
        String pkg = act.getPackageName();
        try {
            Intent i = new Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=" + pkg));
            i.setPackage(PlayStore);
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            act.startActivity(i);
            return true;
        } catch (Throwable e) {
            try {
                Intent i = new Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=" + pkg));
                i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                act.startActivity(i);
                return true;
            } catch (Throwable e2) {
                return false;
            }
        }
    }

    static void send(String go, String method, String msg) {
        try { UnityPlayer.UnitySendMessage(go, method, msg); } catch (Throwable ignored) { }
    }
}
