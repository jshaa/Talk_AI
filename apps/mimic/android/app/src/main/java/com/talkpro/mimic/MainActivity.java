package com.talkpro.mimic;

import android.os.Bundle;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebView;
import com.getcapacitor.Bridge;
import com.getcapacitor.BridgeActivity;
import com.getcapacitor.BridgeWebViewClient;
import java.util.HashMap;
import java.util.Map;

public class MainActivity extends BridgeActivity {

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        bridge.setWebViewClient(new IsolatingWebViewClient(bridge));
    }

    /**
     * Serves the bundled web app with COOP/COEP headers so the page is cross-origin isolated and the
     * llama.cpp WebAssembly runtime can use threads (SharedArrayBuffer). Only bundled files pass through
     * the local server; model downloads from Hugging Face are untouched.
     */
    static final class IsolatingWebViewClient extends BridgeWebViewClient {

        IsolatingWebViewClient(Bridge bridge) {
            super(bridge);
        }

        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            WebResourceResponse response = super.shouldInterceptRequest(view, request);
            if (response != null) {
                Map<String, String> headers = new HashMap<>();
                if (response.getResponseHeaders() != null) {
                    headers.putAll(response.getResponseHeaders());
                }
                headers.put("Cross-Origin-Opener-Policy", "same-origin");
                headers.put("Cross-Origin-Embedder-Policy", "require-corp");
                headers.put("Cross-Origin-Resource-Policy", "same-origin");
                response.setResponseHeaders(headers);
            }
            return response;
        }
    }
}
