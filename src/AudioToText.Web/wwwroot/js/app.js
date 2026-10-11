(function ($) {
  "use strict";

  var $fileInput = $("#fileInput");
  var $dropZone = $("#dropZone");
  var $status = $("#status");
  var $transcript = $("#transcript");
  var $copyBtn = $("#copyBtn");
  var $downloadBtn = $("#downloadBtn");
  var $clearBtn = $("#clearBtn");
  var $recordBtn = $("#recordBtn");
  var $language = $("#language");
  var busy = false;
  var mediaRecorder = null;
  var recordedChunks = [];

  function setStatus(message, kind) {
    $status
      .text(message)
      .toggleClass("is-error", kind === "error")
      .toggleClass("is-busy", kind === "busy");
  }

  function setBusy(next) {
    busy = next;
    $("#recordBtn, #language").prop("disabled", next);
    $("#pickFileBtn").toggleClass("disabled", next).attr("aria-disabled", next ? "true" : "false");
    $fileInput.prop("disabled", next);
  }

  function openFilePicker() {
    if (busy) {
      return;
    }
    var input = $fileInput.get(0);
    if (input) {
      input.click();
    }
  }

  function setTranscript(text) {
    $transcript.val(text || "");
    var hasText = Boolean(text);
    $copyBtn.add($downloadBtn).add($clearBtn).prop("disabled", !hasText);
    if (!hasText) {
      clearTldr();
    }
  }

  function clearTldr() {
    $("#tldrPanel").addClass("d-none");
    $("#tldrBody").empty();
    $("#tldrMethodLabel").text("Riassunto locale (estrattivo)");
  }

  function renderTldr(tldr) {
    if (!tldr || (!tldr.summary && !(tldr.bullets && tldr.bullets.length))) {
      clearTldr();
      return;
    }

    $("#tldrMethodLabel").text(tldr.methodLabel || "Riassunto locale (estrattivo)");
    var $body = $("#tldrBody").empty();
    if (tldr.bullets && tldr.bullets.length >= 2) {
      var $ul = $("<ul></ul>");
      tldr.bullets.forEach(function (b) {
        $ul.append($("<li></li>").text(b));
      });
      $body.append($ul);
    } else {
      $body.append($("<p class='mb-0'></p>").text(tldr.summary || ""));
    }
    $("#tldrPanel").removeClass("d-none");
  }

  function getTldrPlainText() {
    var bullets = [];
    $("#tldrBody li").each(function () {
      bullets.push("- " + $(this).text());
    });
    if (bullets.length) {
      return bullets.join("\n");
    }
    return $("#tldrBody").text().trim();
  }

  function transcribeFile(file) {
    if (!file || busy) {
      return;
    }

    setBusy(true);
    setTranscript("");
    clearTldr();
    setStatus("Trascrizione in corso… (" + file.name + ")", "busy");

    var form = new FormData();
    form.append("file", file, file.name || "audio.webm");
    form.append("language", $language.val());

    $.ajax({
      url: "/api/transcriptions",
      method: "POST",
      data: form,
      processData: false,
      contentType: false
    })
      .done(function (data) {
        setTranscript(data.text || "");
        renderTldr(data.tldr);
        setStatus("Trascrizione completata.", "ok");
      })
      .fail(function (xhr) {
        var message =
          (xhr.responseJSON && xhr.responseJSON.error) ||
          "Trascrizione non riuscita.";
        setStatus(message, "error");
      })
      .always(function () {
        setBusy(false);
      });
  }

  // Label[for=fileInput] opens the dialog natively; stop dropZone from double-firing.
  $("#pickFileBtn").on("click", function (e) {
    e.stopPropagation();
    if (busy) {
      e.preventDefault();
    }
  });

  $dropZone.on("click", function (e) {
    if ($(e.target).closest("#pickFileBtn, #fileInput").length) {
      return;
    }
    openFilePicker();
  });

  $dropZone.on("keydown", function (e) {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      openFilePicker();
    }
  });

  $fileInput.on("change", function () {
    var file = this.files && this.files[0];
    if (file) {
      transcribeFile(file);
    }
    this.value = "";
  });

  $dropZone
    .on("dragenter dragover", function (e) {
      e.preventDefault();
      e.stopPropagation();
      $dropZone.addClass("is-dragover");
    })
    .on("dragleave drop", function (e) {
      e.preventDefault();
      e.stopPropagation();
      $dropZone.removeClass("is-dragover");
    })
    .on("drop", function (e) {
      var file = e.originalEvent.dataTransfer.files[0];
      if (file) {
        transcribeFile(file);
      }
    });

  $copyBtn.on("click", function () {
    var text = $transcript.val();
    if (!text) {
      return;
    }
    navigator.clipboard.writeText(text).then(
      function () {
        setStatus("Testo copiato.", "ok");
      },
      function () {
        setStatus("Copia non riuscita.", "error");
      }
    );
  });

  $downloadBtn.on("click", function () {
    var text = $transcript.val();
    if (!text) {
      return;
    }
    var blob = new Blob([text], { type: "text/plain;charset=utf-8" });
    var url = URL.createObjectURL(blob);
    var a = document.createElement("a");
    a.href = url;
    a.download = "trascrizione.txt";
    a.click();
    URL.revokeObjectURL(url);
  });

  $clearBtn.on("click", function () {
    setTranscript("");
    clearTldr();
    setStatus("Pronto.", "ok");
  });

  $("#copyTldrBtn").on("click", function () {
    var text = getTldrPlainText();
    if (!text) {
      return;
    }
    navigator.clipboard.writeText(text).then(
      function () {
        setStatus("TLDR copiato.", "ok");
      },
      function () {
        setStatus("Copia TLDR non riuscita.", "error");
      }
    );
  });

  $("#regenTldrBtn").on("click", function () {
    var text = $transcript.val();
    if (!text) {
      return;
    }
    setStatus("Ricalcolo riassunto…", "busy");
    $.ajax({
      url: "/api/transcriptions/tldr",
      method: "POST",
      contentType: "application/json",
      data: JSON.stringify({ text: text }),
      dataType: "json"
    })
      .done(function (data) {
        renderTldr(data.tldr);
        setStatus("Riassunto aggiornato.", "ok");
      })
      .fail(function (xhr) {
        setStatus(
          (xhr.responseJSON && xhr.responseJSON.error) || "Riassunto non riuscito.",
          "error"
        );
      });
  });

  async function toggleRecording() {
    if (busy && !mediaRecorder) {
      return;
    }

    if (mediaRecorder && mediaRecorder.state === "recording") {
      mediaRecorder.stop();
      return;
    }

    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      setStatus("Microfono non supportato in questo browser.", "error");
      return;
    }

    try {
      var stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      recordedChunks = [];
      mediaRecorder = new MediaRecorder(stream);
      mediaRecorder.ondataavailable = function (event) {
        if (event.data.size > 0) {
          recordedChunks.push(event.data);
        }
      };
      mediaRecorder.onstop = function () {
        stream.getTracks().forEach(function (track) {
          track.stop();
        });
        $recordBtn.removeClass("is-recording").text("Registra");
        var blob = new Blob(recordedChunks, { type: mediaRecorder.mimeType || "audio/webm" });
        var file = new File([blob], "registrazione.webm", { type: blob.type });
        mediaRecorder = null;
        transcribeFile(file);
      };
      mediaRecorder.start();
      $recordBtn.addClass("is-recording").text("Stop");
      setStatus("Registrazione in corso…", "busy");
    } catch (err) {
      setStatus("Permesso microfono negato o non disponibile.", "error");
    }
  }

  $recordBtn.on("click", function () {
    toggleRecording();
  });

  // —— Impostazioni (provider + locale + Cloudflare secrets) ——
  var $settingsMessage = $("#settingsMessage");
  var $accountIdStatus = $("#accountIdStatus");
  var $apiTokenStatus = $("#apiTokenStatus");
  var $accountIdInput = $("#accountIdInput");
  var $apiTokenInput = $("#apiTokenInput");
  var $providerSelect = $("#providerSelect");
  var $localModelSelect = $("#localModelSelect");
  var downloadPollTimer = null;
  var ffmpegPollTimer = null;
  var modelCatalog = [];

  function setSettingsMessage(message, kind) {
    $settingsMessage
      .text(message || "")
      .toggleClass("is-error", kind === "error")
      .toggleClass("is-busy", kind === "busy");
  }

  function renderStatusBadge($el, configured, readyText, missingText) {
    $el
      .text(configured ? readyText || "Configurato" : missingText || "Non configurato")
      .removeClass("text-bg-secondary text-bg-success text-bg-warning")
      .addClass(configured ? "text-bg-success" : "text-bg-warning");
  }

  function applyFfmpegStatus(ffmpeg) {
    ffmpeg = ffmpeg || {};
    var available = Boolean(ffmpeg.available);
    var source = ffmpeg.source || (available ? "Trovato" : "Mancante");
    $("#ffmpegStatusBadge")
      .text(source)
      .removeClass("text-bg-secondary text-bg-success text-bg-warning")
      .addClass(available ? "text-bg-success" : "text-bg-warning");
    $("#ffmpegPathLabel").text(ffmpeg.path || "");
    $("#ffmpegMessageLine").text(ffmpeg.message || "");
    if (ffmpeg.requirements) {
      $("#ffmpegReqsList").html(
        "<li>" +
          String(ffmpeg.requirements)
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;") +
          "</li>" +
          "<li>Formati: ogg/opus/m4a/webm (WhatsApp)</li>" +
          "<li>WAV/MP3: spesso ok senza ffmpeg</li>"
      );
    }
    updateFfmpegProgressUi(ffmpeg);
    $("#localFfmpegLabel").text(
      available ? source : "non trovato — usa Scarica ffmpeg"
    );
  }

  function updateFfmpegProgressUi(ffmpeg) {
    var downloading = Boolean(ffmpeg.isDownloading);
    var pct = ffmpeg.downloadPercent != null ? ffmpeg.downloadPercent : 0;
    var total = ffmpeg.bytesTotal || 0;
    var done = ffmpeg.bytesDownloaded || 0;
    var showBar = downloading || pct === 100;

    $("#ffmpegProgressWrap").toggleClass("d-none", !showBar);
    $("#ffmpegProgressBar").css("width", pct + "%");
    $("#ffmpegProgressPct").text(pct + "%");
    $("#ffmpegProgressLabel").text(
      downloading
        ? formatBytes(done) + " / " + formatBytes(total)
        : ffmpeg.message || "ffmpeg"
    );
    $("#ffmpegProgressWrap .progress").attr("aria-valuenow", pct);
    $("#downloadFfmpegBtn").prop("disabled", downloading);
    $("#prepareLocalBtn").prop("disabled", downloading);
    $("#cancelFfmpegBtn").toggleClass("d-none", !downloading);

    if (downloading) {
      startFfmpegPolling();
    } else {
      stopFfmpegPolling();
      if (pct === 100) {
        setTimeout(function () {
          if (!$("#downloadFfmpegBtn").prop("disabled")) {
            $("#ffmpegProgressWrap").addClass("d-none");
          }
        }, 2500);
      }
    }
  }

  function startFfmpegPolling() {
    if (ffmpegPollTimer) {
      return;
    }
    ffmpegPollTimer = setInterval(function () {
      $.ajax({
        url: "/api/settings/ffmpeg/download-progress",
        method: "GET",
        dataType: "json"
      })
        .done(function (ffmpeg) {
          applyFfmpegStatus(ffmpeg);
          if (!ffmpeg.isDownloading) {
            stopFfmpegPolling();
            loadSettingsStatus();
          }
        })
        .fail(function () {
          stopFfmpegPolling();
          setSettingsMessage("Errore nel monitoraggio download ffmpeg.", "error");
          $("#downloadFfmpegBtn").prop("disabled", false);
          $("#prepareLocalBtn").prop("disabled", false);
          $("#cancelFfmpegBtn").addClass("d-none");
          $("#ffmpegProgressWrap").addClass("d-none");
        });
    }, 500);
  }

  function stopFfmpegPolling() {
    if (ffmpegPollTimer) {
      clearInterval(ffmpegPollTimer);
      ffmpegPollTimer = null;
    }
  }

  function applyFullSettings(data) {
    var provider = data.provider || "CloudflareRest";
    $providerSelect.val(provider === "Local" ? "Local" : "CloudflareRest");
    $("#providerStatus")
      .text(provider === "Local" ? "Locale" : "Cloudflare")
      .removeClass("text-bg-secondary text-bg-success text-bg-warning")
      .addClass("text-bg-success");

    if (data.localModel) {
      $localModelSelect.val(data.localModel);
    }

    var cf = data.cloudflare || data;
    renderStatusBadge($accountIdStatus, Boolean(cf.accountIdConfigured));
    renderStatusBadge($apiTokenStatus, Boolean(cf.apiTokenConfigured));

    var local = data.local || {};
    if (local.models && local.models.length) {
      modelCatalog = local.models;
    }
    renderStatusBadge(
      $("#localModelStatus"),
      Boolean(local.modelReady),
      "Modello pronto",
      "Da scaricare"
    );
    $("#localRuntimeLabel").text(local.runtime || "…");
    $("#localModelsPath").text(local.modelsDirectory || "…");
    $("#localMessageLine").text(local.message || "");
    if (local.hardwareHint) {
      $("#localHardwareHint").text(local.hardwareHint);
    }
    if (local.ffmpegHint) {
      $("#localFfmpegHint").text(local.ffmpegHint);
    }
    updateModelRequirements($localModelSelect.val());
    updateDownloadProgressUi(local);
    applyFfmpegStatus(data.ffmpeg || {
      available: local.ffmpegAvailable,
      source: local.ffmpegSource || (local.ffmpegAvailable ? "Trovato" : "Mancante")
    });
  }

  function findModelInfo(id) {
    for (var i = 0; i < modelCatalog.length; i++) {
      if (modelCatalog[i].id === id) {
        return modelCatalog[i];
      }
    }
    return null;
  }

  function updateModelRequirements(modelId) {
    var info = findModelInfo(modelId);
    if (!info) {
      return;
    }
    $("#reqSize").text("Download / disco: " + info.downloadSizeLabel);
    $("#reqRam").text("RAM: " + info.ramHint);
    $("#reqVram").text("VRAM: " + info.vramHint);
    $("#reqRecommend").text("Consiglio: " + info.recommend);
  }

  function formatBytes(n) {
    if (!n || n < 0) {
      return "0 B";
    }
    if (n < 1024) {
      return n + " B";
    }
    var mb = n / (1024 * 1024);
    return mb >= 1024 ? (mb / 1024).toFixed(2) + " GB" : mb.toFixed(1) + " MB";
  }

  function updateDownloadProgressUi(local) {
    var downloading = Boolean(local.isDownloading);
    var pct = local.downloadPercent != null ? local.downloadPercent : 0;
    var total = local.bytesTotal || 0;
    var done = local.bytesDownloaded || 0;
    var showBar = downloading || pct === 100;

    $("#downloadProgressWrap").toggleClass("d-none", !showBar);
    $("#downloadProgressBar").css("width", pct + "%");
    $("#downloadProgressPct").text(pct + "%");
    $("#downloadProgressLabel").text(
      downloading
        ? formatBytes(done) + " / " + formatBytes(total)
        : local.message || "Download"
    );
    $("#downloadProgressWrap .progress").attr("aria-valuenow", pct);

    $("#downloadModelBtn").prop("disabled", downloading);
    $("#cancelDownloadBtn").toggleClass("d-none", !downloading);
    $("#localModelStatus").toggleClass("d-none", downloading);

    if (downloading) {
      startDownloadPolling();
    } else {
      stopDownloadPolling();
      if (pct === 100) {
        setTimeout(function () {
          if (!$("#downloadModelBtn").prop("disabled")) {
            $("#downloadProgressWrap").addClass("d-none");
          }
        }, 2500);
      } else if (!downloading && pct === 0) {
        $("#downloadProgressWrap").addClass("d-none");
      }
    }
  }

  function startDownloadPolling() {
    if (downloadPollTimer) {
      return;
    }
    downloadPollTimer = setInterval(function () {
      $.ajax({
        url: "/api/settings/local/download-progress",
        method: "GET",
        dataType: "json"
      })
        .done(function (local) {
          updateDownloadProgressUi(local);
          $("#localMessageLine").text(local.message || "");
          renderStatusBadge(
            $("#localModelStatus"),
            Boolean(local.modelReady),
            "Modello pronto",
            "Da scaricare"
          );
          if (!local.isDownloading) {
            stopDownloadPolling();
            loadSettingsStatus();
          }
        })
        .fail(function () {
          stopDownloadPolling();
          setSettingsMessage("Errore nel monitoraggio download.", "error");
          $("#downloadModelBtn").prop("disabled", false);
          $("#cancelDownloadBtn").addClass("d-none");
          $("#downloadProgressWrap").addClass("d-none");
        });
    }, 500);
  }

  function stopDownloadPolling() {
    if (downloadPollTimer) {
      clearInterval(downloadPollTimer);
      downloadPollTimer = null;
    }
  }

  function assertNoSecretsLeaked(payloadText, candidates) {
    var lower = (payloadText || "").toLowerCase();
    candidates.forEach(function (value) {
      if (value && lower.indexOf(String(value).toLowerCase()) !== -1) {
        console.warn("Settings response unexpectedly contained a secret-like value.");
      }
    });
  }

  function loadSettingsStatus() {
    return $.ajax({
      url: "/api/settings",
      method: "GET",
      dataType: "json"
    })
      .done(function (data, _status, xhr) {
        applyFullSettings(data);
        assertNoSecretsLeaked(xhr.responseText, [
          $accountIdInput.val(),
          $apiTokenInput.val()
        ]);
      })
      .fail(function () {
        setSettingsMessage("Impossibile leggere lo stato delle impostazioni.", "error");
      });
  }

  function saveCloudflareField(body, $input, successMessage) {
    var typed = $input.val();
    setSettingsMessage("Salvataggio…", "busy");
    $.ajax({
      url: "/api/settings/cloudflare",
      method: "PUT",
      contentType: "application/json",
      data: JSON.stringify(body),
      dataType: "json"
    })
      .done(function (data, _status, xhr) {
        $input.val("");
        renderStatusBadge($accountIdStatus, Boolean(data.accountIdConfigured));
        renderStatusBadge($apiTokenStatus, Boolean(data.apiTokenConfigured));
        assertNoSecretsLeaked(xhr.responseText, [typed]);
        setSettingsMessage(successMessage, "ok");
        loadSettingsStatus();
      })
      .fail(function (xhr) {
        var message =
          (xhr.responseJSON && xhr.responseJSON.error) ||
          "Salvataggio non riuscito.";
        setSettingsMessage(message, "error");
      });
  }

  $("#saveProviderBtn").on("click", function () {
    setSettingsMessage("Salvataggio provider…", "busy");
    $.ajax({
      url: "/api/settings/provider",
      method: "PUT",
      contentType: "application/json",
      data: JSON.stringify({ provider: $providerSelect.val() }),
      dataType: "json"
    })
      .done(function (data) {
        applyFullSettings(data);
        setSettingsMessage("Provider aggiornato.", "ok");
      })
      .fail(function (xhr) {
        setSettingsMessage(
          (xhr.responseJSON && xhr.responseJSON.error) || "Salvataggio provider non riuscito.",
          "error"
        );
      });
  });

  $("#saveLocalModelBtn").on("click", function () {
    setSettingsMessage("Salvataggio modello…", "busy");
    $.ajax({
      url: "/api/settings/local-model",
      method: "PUT",
      contentType: "application/json",
      data: JSON.stringify({ model: $localModelSelect.val() }),
      dataType: "json"
    })
      .done(function (data) {
        applyFullSettings(data);
        setSettingsMessage("Modello locale selezionato.", "ok");
      })
      .fail(function (xhr) {
        setSettingsMessage(
          (xhr.responseJSON && xhr.responseJSON.error) || "Salvataggio modello non riuscito.",
          "error"
        );
      });
  });

  $localModelSelect.on("change", function () {
    updateModelRequirements($(this).val());
  });

  $("#downloadModelBtn").on("click", function () {
    if ($("#downloadModelBtn").prop("disabled")) {
      return;
    }
    setSettingsMessage("Download avviato…", "busy");
    $("#downloadModelBtn").prop("disabled", true);
    $("#downloadProgressWrap").removeClass("d-none");
    $("#downloadProgressBar").css("width", "0%");
    $("#downloadProgressPct").text("0%");
    $.ajax({
      url: "/api/settings/local/download-model",
      method: "POST",
      contentType: "application/json",
      data: JSON.stringify({ model: $localModelSelect.val() }),
      dataType: "json"
    })
      .done(function (data) {
        applyFullSettings(data);
        startDownloadPolling();
      })
      .fail(function (xhr) {
        setSettingsMessage(
          (xhr.responseJSON && xhr.responseJSON.error) || "Download modello non riuscito.",
          "error"
        );
        $("#downloadModelBtn").prop("disabled", false);
        $("#cancelDownloadBtn").addClass("d-none");
        $("#downloadProgressWrap").addClass("d-none");
        $("#downloadProgressBar").css("width", "0%");
        stopDownloadPolling();
      });
  });

  $("#cancelDownloadBtn").on("click", function () {
    $.ajax({
      url: "/api/settings/local/download-cancel",
      method: "POST",
      dataType: "json"
    })
      .done(function (data) {
        applyFullSettings(data);
        setSettingsMessage("Download annullato.", "ok");
      })
      .fail(function () {
        setSettingsMessage("Annullamento non riuscito.", "error");
      });
  });

  $("#downloadFfmpegBtn").on("click", function () {
    if ($("#downloadFfmpegBtn").prop("disabled")) {
      return;
    }
    setSettingsMessage("Download ffmpeg avviato…", "busy");
    $("#downloadFfmpegBtn").prop("disabled", true);
    $("#ffmpegProgressWrap").removeClass("d-none");
    $("#ffmpegProgressBar").css("width", "0%");
    $("#ffmpegProgressPct").text("0%");
    $.ajax({
      url: "/api/settings/ffmpeg/download",
      method: "POST",
      dataType: "json"
    })
      .done(function (data) {
        applyFullSettings(data);
        startFfmpegPolling();
      })
      .fail(function (xhr) {
        setSettingsMessage(
          (xhr.responseJSON && xhr.responseJSON.error) ||
            "Download ffmpeg non riuscito. Controlla la rete e riprova.",
          "error"
        );
        $("#downloadFfmpegBtn").prop("disabled", false);
        $("#prepareLocalBtn").prop("disabled", false);
        $("#cancelFfmpegBtn").addClass("d-none");
        $("#ffmpegProgressWrap").addClass("d-none");
        stopFfmpegPolling();
      });
  });

  $("#prepareLocalBtn").on("click", function () {
    if ($("#prepareLocalBtn").prop("disabled")) {
      return;
    }
    setSettingsMessage("Preparazione ambiente Locale…", "busy");
    $.ajax({
      url: "/api/settings/local/prepare",
      method: "POST",
      dataType: "json"
    })
      .done(function (data) {
        applyFullSettings(data);
        if (data.ffmpeg && data.ffmpeg.isDownloading) {
          startFfmpegPolling();
          setSettingsMessage("Download ffmpeg in corso (ambiente Locale).", "busy");
        } else if (data.ffmpeg && data.ffmpeg.available) {
          setSettingsMessage("Ambiente Locale: ffmpeg pronto. Scarica anche il modello se manca.", "ok");
        } else {
          setSettingsMessage(data.ffmpeg && data.ffmpeg.message ? data.ffmpeg.message : "Ambiente Locale aggiornato.", "ok");
        }
      })
      .fail(function (xhr) {
        setSettingsMessage(
          (xhr.responseJSON && xhr.responseJSON.error) ||
            "Preparazione ambiente non riuscita.",
          "error"
        );
      });
  });

  $("#cancelFfmpegBtn").on("click", function () {
    $.ajax({
      url: "/api/settings/ffmpeg/download-cancel",
      method: "POST",
      dataType: "json"
    })
      .done(function (data) {
        applyFullSettings(data);
        setSettingsMessage("Download ffmpeg annullato.", "ok");
      })
      .fail(function () {
        setSettingsMessage("Annullamento ffmpeg non riuscito.", "error");
      });
  });

  $("#saveAccountIdBtn").on("click", function () {
    var value = ($accountIdInput.val() || "").trim();
    if (!value) {
      setSettingsMessage("Inserisci un Account ID.", "error");
      return;
    }
    saveCloudflareField({ accountId: value }, $accountIdInput, "Account ID aggiornato.");
  });

  $("#saveApiTokenBtn").on("click", function () {
    var value = ($apiTokenInput.val() || "").trim();
    if (!value) {
      setSettingsMessage("Inserisci un API Token.", "error");
      return;
    }
    saveCloudflareField({ apiToken: value }, $apiTokenInput, "API Token aggiornato.");
  });

  $("#clearAccountIdBtn").on("click", function () {
    setSettingsMessage("Rimozione…", "busy");
    $.ajax({ url: "/api/settings/cloudflare/account-id", method: "DELETE", dataType: "json" })
      .done(function () {
        $accountIdInput.val("");
        setSettingsMessage("Account ID rimosso.", "ok");
        loadSettingsStatus();
      })
      .fail(function () {
        setSettingsMessage("Rimozione Account ID non riuscita.", "error");
      });
  });

  $("#clearApiTokenBtn").on("click", function () {
    setSettingsMessage("Rimozione…", "busy");
    $.ajax({ url: "/api/settings/cloudflare/api-token", method: "DELETE", dataType: "json" })
      .done(function () {
        $apiTokenInput.val("");
        setSettingsMessage("API Token rimosso.", "ok");
        loadSettingsStatus();
      })
      .fail(function () {
        setSettingsMessage("Rimozione API Token non riuscita.", "error");
      });
  });

  function showSection(name) {
    var isSettings = name === "settings";
    $("#sectionTranscribe").toggleClass("d-none", isSettings);
    $("#sectionSettings").toggleClass("d-none", !isSettings);
    $("#navTranscribe").toggleClass("active", !isSettings);
    $("#navSettings").toggleClass("active", isSettings);
    if (isSettings) {
      loadSettingsStatus();
    }
  }

  $("#navTranscribe").on("click", function () {
    showSection("transcribe");
  });
  $("#navSettings").on("click", function () {
    showSection("settings");
  });
})(jQuery);
