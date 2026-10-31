package com.kiotrandev.gomoku;

import android.webkit.CookieManager;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onPause() {
        super.onPause();
        // WebView writes cookies lazily; persist the sign-in cookie before Android may kill the app in the background.
        CookieManager.getInstance().flush();
    }
}
