declare module "pdfjs-dist/legacy/build/pdf.worker.mjs" {
  export const WorkerMessageHandler: unknown;
}

declare module "virtual:reasonix-pdf-assets" {
  const resources: Record<string, Record<string, string>>;
  export default resources;
}
