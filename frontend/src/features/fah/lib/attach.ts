// Attaching files to a FAH message. The server has the final say on every
// limit (FahFiles); these only save an upload that would be refused anyway.

export const MAX_FILES_PER_MESSAGE = 5;

export const ACCEPT = ".png,.jpg,.jpeg,.gif,.webp,.pdf,.xlsx,.xlsm,.csv,.docx";

// The model sees an image at most about this size, so a bigger one is only
// a slower upload.
const MAX_IMAGE_SIDE = 1568;
const KEEP_IMAGE_BELOW = 1.5 * 1024 * 1024;

// A photo or large screenshot shrunk before upload: quicker to send and
// cheaper for FAH to read. GIFs (possibly animated) and anything the browser
// can't decode go as they are.
export async function shrinkImage(file: File): Promise<File> {
  if (!/^image\/(png|jpeg|webp)$/.test(file.type)) return file;

  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file);
  } catch {
    return file;
  }

  const scale = Math.min(1, MAX_IMAGE_SIDE / Math.max(bitmap.width, bitmap.height));
  if (scale === 1 && file.size <= KEEP_IMAGE_BELOW) {
    bitmap.close();
    return file;
  }

  const canvas = document.createElement("canvas");
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext("2d")?.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();

  // PNG stays PNG (sharp text in screenshots) unless it's still large.
  const toBlob = (type: string) =>
    new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, 0.88));
  let blob = file.type === "image/png" ? await toBlob("image/png") : null;
  if (!blob || blob.size > KEEP_IMAGE_BELOW * 2) blob = await toBlob("image/jpeg");
  if (!blob || blob.size >= file.size) return file;

  const name = blob.type === "image/jpeg" ? file.name.replace(/\.(png|webp)$/i, ".jpg") : file.name;
  return new File([blob], name, { type: blob.type });
}

// Dropped items that are folders: FAH takes files only.
export function droppedFolders(data: DataTransfer): number {
  return Array.from(data.items).filter((item) => item.webkitGetAsEntry?.()?.isDirectory).length;
}
