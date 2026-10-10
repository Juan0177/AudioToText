# Voce

Trascrizione vocale **nel browser** con Whisper. Pensata per i messaggi audio di WhatsApp: carichi il file (o registri) e ottieni il testo sul dispositivo, senza bot a pagamento e senza API cloud.

## Funzionalità

- Caricamento drag-and-drop / file picker (ogg, opus, m4a, mp3, wav, webm, …)
- Registrazione dal microfono
- Whisper locale via [`@huggingface/transformers`](https://huggingface.co/docs/transformers.js) (modelli Tiny / Base / Small)
- Lingua italiana o rilevamento automatico
- Copia e download `.txt` della trascrizione

## Sviluppo locale

```bash
npm install
npm run dev
```

Apri [http://127.0.0.1:43127](http://127.0.0.1:43127).

Al primo avvio il browser scarica il modello Whisper (poi resta in cache).

```bash
npm run build
npm run preview
```

## Accessibile da tutte le parti

L’app è statica: l’audio viene elaborato **sul dispositivo** di chi apre la pagina.

1. Crea la repository GitHub (pulsante **Create repo** nell’agent, se non l’hai già fatto).
2. Collega il repo a **Cloudflare Pages**, **Vercel** o **Netlify**.
3. Build command: `npm run build`
4. Output directory: `dist`
5. Apri l’URL pubblico dal telefono o da qualsiasi PC.

Suggerimento telefono: aggiungi la pagina alla schermata Home per aprirla come un’app.

## Note

- Su alcuni browser i vocali `.ogg` di WhatsApp si aprono meglio se esportati/condivisi come file; se la decodifica fallisce, converti in m4a/mp3/wav.
- Il modello Tiny è il più adatto al telefono; Small è più accurato ma più lento al primo download.
- Serve un browser moderno con WebAssembly.
