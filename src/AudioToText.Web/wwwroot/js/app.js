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
  }

  function transcribeFile(file) {
    if (!file || busy) {
      return;
    }

    setBusy(true);
    setTranscript("");
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
    setStatus("Pronto.", "ok");
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

  // —— Impostazioni Cloudflare (status only; never echo secrets) ——
  var $settingsMessage = $("#settingsMessage");
  var $accountIdStatus = $("#accountIdStatus");
  var $apiTokenStatus = $("#apiTokenStatus");
  var $accountIdInput = $("#accountIdInput");
  var $apiTokenInput = $("#apiTokenInput");

  function setSettingsMessage(message, kind) {
    $settingsMessage
      .text(message || "")
      .toggleClass("is-error", kind === "error")
      .toggleClass("is-busy", kind === "busy");
  }

  function renderStatusBadge($el, configured) {
    $el
      .text(configured ? "Configurato" : "Non configurato")
      .removeClass("text-bg-secondary text-bg-success text-bg-warning")
      .addClass(configured ? "text-bg-success" : "text-bg-warning");
  }

  function applySettingsStatus(data) {
    renderStatusBadge($accountIdStatus, Boolean(data.accountIdConfigured));
    renderStatusBadge($apiTokenStatus, Boolean(data.apiTokenConfigured));
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
      url: "/api/settings/cloudflare",
      method: "GET",
      dataType: "json"
    })
      .done(function (data, _status, xhr) {
        applySettingsStatus(data);
        assertNoSecretsLeaked(xhr.responseText, [
          $accountIdInput.val(),
          $apiTokenInput.val()
        ]);
      })
      .fail(function () {
        setSettingsMessage("Impossibile leggere lo stato delle impostazioni.", "error");
      });
  }

  function saveField(body, $input, successMessage) {
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
        applySettingsStatus(data);
        assertNoSecretsLeaked(xhr.responseText, [typed]);
        setSettingsMessage(successMessage, "ok");
      })
      .fail(function (xhr) {
        var message =
          (xhr.responseJSON && xhr.responseJSON.error) ||
          "Salvataggio non riuscito.";
        setSettingsMessage(message, "error");
      });
  }

  $("#saveAccountIdBtn").on("click", function () {
    var value = ($accountIdInput.val() || "").trim();
    if (!value) {
      setSettingsMessage("Inserisci un Account ID.", "error");
      return;
    }
    saveField({ accountId: value }, $accountIdInput, "Account ID aggiornato.");
  });

  $("#saveApiTokenBtn").on("click", function () {
    var value = ($apiTokenInput.val() || "").trim();
    if (!value) {
      setSettingsMessage("Inserisci un API Token.", "error");
      return;
    }
    saveField({ apiToken: value }, $apiTokenInput, "API Token aggiornato.");
  });

  $("#clearAccountIdBtn").on("click", function () {
    setSettingsMessage("Rimozione…", "busy");
    $.ajax({ url: "/api/settings/cloudflare/account-id", method: "DELETE", dataType: "json" })
      .done(function (data) {
        $accountIdInput.val("");
        applySettingsStatus(data);
        setSettingsMessage("Account ID rimosso.", "ok");
      })
      .fail(function () {
        setSettingsMessage("Rimozione Account ID non riuscita.", "error");
      });
  });

  $("#clearApiTokenBtn").on("click", function () {
    setSettingsMessage("Rimozione…", "busy");
    $.ajax({ url: "/api/settings/cloudflare/api-token", method: "DELETE", dataType: "json" })
      .done(function (data) {
        $apiTokenInput.val("");
        applySettingsStatus(data);
        setSettingsMessage("API Token rimosso.", "ok");
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
