package com.zero0084.omm;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;

import com.unity3d.player.UnityPlayer;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.Arrays;

// データの引き継ぎ(2026-10-14): Android 標準の保存先/ファイルの選択(Storage Access Framework)。
// 権限は要らない(ユーザーが選んだ1つのファイルだけを読み書きする)。マニフェストを変えないよう、結果は見えない Fragment で受け取る。
// 結果は UnitySendMessage(go, method, "export|ok|名前" / "import|ok|名前" / "...|cancel" / "...|error|理由")。
@SuppressWarnings("deprecation")
public class SaveTransferBridge {
    static final int MaxImportBytes = 4 * 1024 * 1024;

    public static void exportFile(Activity act, String fileName, String srcPath, String go, String method) { start(act, 1, fileName, srcPath, go, method); }
    public static void importFile(Activity act, String dstPath, String go, String method) { start(act, 2, null, dstPath, go, method); }

    static void start(final Activity act, final int mode, final String name, final String path, final String go, final String method) {
        act.runOnUiThread(new Runnable() {
            public void run() {
                try {
                    PickFragment f = new PickFragment();
                    Bundle b = new Bundle();
                    b.putInt("mode", mode); b.putString("name", name); b.putString("path", path);
                    b.putString("go", go); b.putString("method", method);
                    f.setArguments(b);
                    act.getFragmentManager().beginTransaction().add(f, "omm_save_transfer_" + mode).commitAllowingStateLoss();
                } catch (Throwable e) {
                    send(go, method, (mode == 1 ? "export" : "import") + "|error|start " + e.getClass().getSimpleName());
                }
            }
        });
    }

    static void send(String go, String method, String msg) {
        try { UnityPlayer.UnitySendMessage(go, method, msg); } catch (Throwable ignored) { }
    }

    static byte[] readAll(InputStream is, int limit) throws IOException {
        try {
            ByteArrayOutputStream bo = new ByteArrayOutputStream();
            byte[] buf = new byte[16384];
            int n;
            while ((n = is.read(buf)) > 0) {
                bo.write(buf, 0, n);
                if (bo.size() > limit) break;
            }
            return bo.toByteArray();
        } finally { is.close(); }
    }

    public static class PickFragment extends Fragment {
        public PickFragment() { }

        @Override public void onCreate(Bundle saved) {
            super.onCreate(saved);
            if (saved != null) return; // 画面の回転などで作り直された: 選ぶ画面はもう開いている
            Bundle a = getArguments();
            Intent i;
            if (a.getInt("mode") == 1) {
                i = new Intent(Intent.ACTION_CREATE_DOCUMENT);
                i.addCategory(Intent.CATEGORY_OPENABLE);
                i.setType("application/json");
                i.putExtra(Intent.EXTRA_TITLE, a.getString("name"));
            } else {
                i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                i.addCategory(Intent.CATEGORY_OPENABLE);
                i.setType("*/*"); // 保存先によっては JSON が application/octet-stream になるため
            }
            try { startActivityForResult(i, 7300 + a.getInt("mode")); }
            catch (Throwable e) { finish("error|nopicker " + e.getClass().getSimpleName()); }
        }

        @Override public void onActivityResult(int request, int result, Intent data) {
            Bundle a = getArguments();
            if (result != Activity.RESULT_OK || data == null || data.getData() == null) { finish("cancel"); return; }
            Uri uri = data.getData();
            try {
                if (a.getInt("mode") == 1) {
                    byte[] bytes = readAll(new FileInputStream(a.getString("path")), 64 * 1024 * 1024);
                    OutputStream os = null;
                    try { os = getActivity().getContentResolver().openOutputStream(uri, "wt"); }
                    catch (Throwable t) { os = getActivity().getContentResolver().openOutputStream(uri, "w"); }
                    if (os == null) throw new IOException("no output");
                    try { os.write(bytes); os.flush(); } finally { os.close(); }
                    // 書けたか読み直して確かめる(読み直せない保存先は「未確認」で返す)
                    String verified = "verified";
                    try {
                        InputStream is = getActivity().getContentResolver().openInputStream(uri);
                        byte[] back = is != null ? readAll(is, bytes.length + 1) : null;
                        if (back == null) verified = "unverified";
                        else if (!Arrays.equals(bytes, back)) { finish("error|verify " + back.length + "/" + bytes.length); return; }
                    } catch (Throwable t) { verified = "unverified"; }
                    finish("ok|" + displayName(uri) + "|" + verified);
                } else {
                    InputStream is = getActivity().getContentResolver().openInputStream(uri);
                    if (is == null) throw new IOException("no input");
                    byte[] bytes = readAll(is, MaxImportBytes);
                    if (bytes.length > MaxImportBytes) { finish("error|toolarge"); return; }
                    File dst = new File(a.getString("path"));
                    FileOutputStream fo = new FileOutputStream(dst);
                    try { fo.write(bytes); fo.flush(); } finally { fo.close(); }
                    finish("ok|" + displayName(uri));
                }
            } catch (Throwable e) {
                finish("error|io " + e.getClass().getSimpleName());
            }
        }

        String displayName(Uri uri) {
            Cursor c = null;
            try {
                c = getActivity().getContentResolver().query(uri, new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null);
                if (c != null && c.moveToFirst()) { String n = c.getString(0); if (n != null) return n.replace('|', '_'); }
            } catch (Throwable ignored) {
            } finally { if (c != null) c.close(); }
            return "";
        }

        void finish(String msg) {
            Bundle a = getArguments();
            send(a.getString("go"), a.getString("method"), (a.getInt("mode") == 1 ? "export|" : "import|") + msg);
            try { getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss(); } catch (Throwable ignored) { }
        }
    }
}
