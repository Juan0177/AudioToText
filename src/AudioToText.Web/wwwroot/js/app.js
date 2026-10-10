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
    $("#pickFileBtn, #recordBtn, #language").prop("disabled", next);
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

  $("#pickFileBtn").on("click", function (e) {
    e.stopPropagation();
    $fileInput.trigger("click");
  });

  $dropZone.on("click", function () {
    if (!busy) {
      $fileInput.trigger("click");
    }
  });

  $dropZone.on("keydown", function (e) {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      $fileInput.trigger("click");
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
})(jQuery);
