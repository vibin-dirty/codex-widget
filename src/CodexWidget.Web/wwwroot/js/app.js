(() => {
    "use strict";

    const presentationEndpoint = "/api/status/presentation";
    const pollingIntervalMilliseconds = 15000;
    const unavailablePercentText = "--";
    const unavailableResetText = "-- --:--";
    const fillToneNormal = "normal";
    const redGateThresholdPercent = 70;
    const yellowGateThresholdPercent = 90;
    const blueSurplusGateThresholdPercent = 110;
    const pinkSurplusGateThresholdPercent = 130;

    const state = {
        presentation: null,
        pollRequestInFlight: false,
        pollingTimerId: null,
        teardownRequested: false,
        teardownController: new AbortController(),
    };

    const elements = {
        mainContent: document.getElementById("main-content"),
        statusBoard: document.getElementById("status-board"),
        statusLine: document.getElementById("status-line"),
        liveAnnouncements: document.getElementById("live-announcements"),
    };

    wireLifecycleHandlers();
    void boot();

    async function boot() {
        renderLoading();
        await refreshPresentation("startup");

        if (elements.mainContent) {
            elements.mainContent.setAttribute("aria-busy", "false");
        }

        startPolling();
    }

    function wireLifecycleHandlers() {
        window.addEventListener("pagehide", stopBrowserRuntime, { once: true });
        window.addEventListener("beforeunload", stopBrowserRuntime, { once: true });
    }

    function stopBrowserRuntime() {
        state.teardownRequested = true;
        stopPolling();
        state.teardownController.abort();
    }

    function startPolling() {
        stopPolling();
        state.pollingTimerId = window.setInterval(() => {
            void runPollingTick();
        }, pollingIntervalMilliseconds);
    }

    function stopPolling() {
        if (typeof state.pollingTimerId === "number") {
            window.clearInterval(state.pollingTimerId);
        }

        state.pollingTimerId = null;
    }

    async function runPollingTick() {
        if (state.teardownRequested) {
            return;
        }

        if (state.pollRequestInFlight) {
            return;
        }

        state.pollRequestInFlight = true;
        try {
            await refreshPresentation("poll");
        } finally {
            state.pollRequestInFlight = false;
        }
    }

    async function refreshPresentation(reason) {
        try {
            const payload = await loadPresentation();
            state.presentation = payload;
            renderPresentation(payload);
            announce("Codex usage loaded.");
        } catch (error) {
            if (isAbortError(error) && state.teardownRequested) {
                return;
            }

            if (state.presentation) {
                setStatusLine("Polling is retrying after a transport failure. Showing the last safe server snapshot.", "error");
                return;
            }

            renderUnavailable(`Usage status unavailable${reason === "startup" ? "." : "; polling will retry."}`);
        }
    }

    async function loadPresentation() {
        const response = await fetch(presentationEndpoint, {
            method: "GET",
            signal: state.teardownController.signal,
            headers: {
                Accept: "application/json",
            },
        });

        if (!response.ok) {
            throw new Error(`Status request failed with HTTP ${response.status}.`);
        }

        const payload = await response.json();
        if (!isObject(payload) || !isObject(payload.compact)) {
            throw new Error("Status response was missing compact presentation data.");
        }

        return payload;
    }

    function renderLoading() {
        clearElement(elements.statusBoard);
        appendToBoard(createElement("p", "empty-state", "Loading usage..."));
        setStatusLine("Loading usage...", "neutral");
    }

    function renderUnavailable(message) {
        clearElement(elements.statusBoard);
        appendToBoard(createElement("p", "empty-state", message));
        setStatusLine("Unavailable", "error");
    }

    function renderPresentation(presentation) {
        clearElement(elements.statusBoard);

        const profiles = arrayOrEmpty(presentation?.compact?.profiles);
        if (profiles.length === 0) {
            appendToBoard(createElement("p", "empty-state", textOrFallback(presentation?.compact?.summaryText, "No profiles available.")));
            setStatusLine("No profiles", "neutral");
            return;
        }

        for (const profile of profiles) {
            elements.statusBoard.appendChild(renderProfile(profile));
        }

        const activeCount = profiles.filter((profile) => profile?.isCurrent === true).length;
        setStatusLine(`${profiles.length} profile${profiles.length === 1 ? "" : "s"}, ${activeCount} active`, "neutral");
    }

    function renderProfile(profile) {
        const card = createElement("article", "profile-card");
        const header = createElement("div", "profile-header");
        header.appendChild(createElement("h2", "profile-name", textOrFallback(profile?.profileDisplayName, "Unknown profile")));

        if (profile?.isCurrent === true) {
            header.appendChild(createElement("span", "active-pill", "Active"));
        }

        card.appendChild(header);

        const bucketCount = appendBucketGroup(card, "main", profile?.mainBucket)
            + appendBucketGroup(card, "spark", profile?.sparkBucket);
        if (bucketCount === 0) {
            card.appendChild(createElement("p", "profile-unavailable", "Usage unavailable."));
        }

        return card;
    }

    function appendBucketGroup(container, label, bucket) {
        if (!isObject(bucket)) {
            return 0;
        }

        const group = createElement("section", "bucket-group");
        group.appendChild(createElement("div", "bucket-label", label));

        const rows = createElement("div", "window-rows");
        rows.appendChild(renderWindowRow("5-hour", bucket.fiveHourWindow, "fiveHour"));
        rows.appendChild(renderWindowRow("weekly", bucket.weeklyWindow, "weekly"));
        group.appendChild(rows);
        container.appendChild(group);
        return 1;
    }

    function renderWindowRow(label, window, windowKind) {
        const row = createElement("div", "usage-row");
        row.dataset.window = windowKind === "weekly" ? "weekly" : "five-hour";
        row.appendChild(createElement("div", "window-label", label));

        const normalizedPercent = normalizePercent(window?.quotaLeftPercent);
        const normalizedTimePercent = normalizePercent(window?.timeLeftPercent);
        const meter = createElement("div", "quota-meter");
        meter.dataset.unavailable = normalizedPercent === null ? "true" : "false";

        const fill = createElement("div", "meter-fill");
        fill.style.width = normalizedPercent === null ? "0%" : `${normalizedPercent}%`;
        fill.dataset.tone = resolveQuotaFillTone(
            normalizedPercent,
            normalizedTimePercent,
            windowKind === "weekly",
        );
        meter.appendChild(fill);

        if (normalizedTimePercent !== null) {
            const marker = createElement("div", "time-marker");
            marker.style.left = `${normalizedTimePercent}%`;
            meter.appendChild(marker);
        }

        row.appendChild(meter);
        row.appendChild(createElement(
            "div",
            "percent-badge",
            normalizedPercent === null ? unavailablePercentText : `${normalizedPercent}%`,
        ));
        row.appendChild(createElement(
            "div",
            "reset-time",
            textOrFallback(window?.endsAtCompactText, unavailableResetText),
        ));
        return row;
    }

    function setStatusLine(text, tone) {
        if (!elements.statusLine) {
            return;
        }

        elements.statusLine.textContent = textOrFallback(text, "");
        elements.statusLine.dataset.tone = tone === "error" ? "error" : "neutral";
    }

    function announce(text) {
        if (elements.liveAnnouncements) {
            elements.liveAnnouncements.textContent = textOrFallback(text, "");
        }
    }

    function appendToBoard(element) {
        if (elements.statusBoard && element) {
            elements.statusBoard.appendChild(element);
        }
    }

    function clearElement(element) {
        if (!element) {
            return;
        }

        while (element.firstChild) {
            element.removeChild(element.firstChild);
        }
    }

    function createElement(tagName, className, text) {
        const element = document.createElement(tagName);
        if (className) {
            element.className = className;
        }

        if (typeof text === "string") {
            element.textContent = text;
        }

        return element;
    }

    function normalizePercent(value) {
        if (typeof value !== "number" || !Number.isFinite(value)) {
            return null;
        }

        const rounded = Math.round(value);
        return Math.max(0, Math.min(100, rounded));
    }

    function resolveQuotaFillTone(quotaLeftPercent, timeLeftPercent, useSurplusFillColors) {
        if (quotaLeftPercent === null || timeLeftPercent === null) {
            return fillToneNormal;
        }

        const gatePercent = calculateUsageGatePercent(quotaLeftPercent, timeLeftPercent);
        if (gatePercent < redGateThresholdPercent) {
            return "red";
        }

        if (gatePercent < yellowGateThresholdPercent) {
            return "yellow";
        }

        if (useSurplusFillColors && gatePercent > pinkSurplusGateThresholdPercent) {
            return "pink";
        }

        if (useSurplusFillColors && gatePercent > blueSurplusGateThresholdPercent) {
            return "blue";
        }

        return fillToneNormal;
    }

    function calculateUsageGatePercent(quotaLeftPercent, timeLeftPercent) {
        const oldGatePercent = (100 * quotaLeftPercent) / timeLeftPercent;
        const newGatePercent = 100 + (quotaLeftPercent - timeLeftPercent);
        if (quotaLeftPercent <= 5) {
            return oldGatePercent;
        }

        if (quotaLeftPercent >= 15) {
            return newGatePercent;
        }

        const transitionWeight = (quotaLeftPercent - 5) / 10;
        return (oldGatePercent * (1 - transitionWeight)) + (newGatePercent * transitionWeight);
    }

    function textOrFallback(value, fallback) {
        return typeof value === "string" && value.trim().length > 0
            ? value.trim()
            : fallback;
    }

    function arrayOrEmpty(value) {
        return Array.isArray(value) ? value : [];
    }

    function isObject(value) {
        return typeof value === "object" && value !== null && !Array.isArray(value);
    }

    function isAbortError(error) {
        return error instanceof DOMException && error.name === "AbortError";
    }
})();
