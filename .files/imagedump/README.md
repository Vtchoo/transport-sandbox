# Trainiax locomotive reference images

Requested selection: **one largest original drawing per locomotive type** from:

https://trainiax.net/mescaleloco-list.php?dtype=UT&scale18=1&scale36=1&scale55=1

The inspected index lists 309 locomotive types. Rolling stock is excluded. `locomotive-index.json` records their manufacturer, model, rebuild flag and source-page URL from the index already retrieved.

**No bulk image download was performed.** During inspection, the site's `robots.txt` explicitly disallowed ChatGPT-User, GPTBot and OAI-SearchBot. Automated collection stopped at that point. This folder contains the index and instructions, not a completed drawing collection.

## Organize an existing local collection

For drawings you obtain manually or through a permitted source, run from the project directory:

```text
node scripts/organize-trainiax-images.mjs "D:\path\to\local\drawings"
```

This command makes no network requests. It scans PNG/GIF files recursively, matches original Trainiax filenames against locomotive models, and copies the largest native pixel-area image per matching type into this folder. Original artwork is not resized, cropped or stripped of credits. Manufacturer/model filename mismatches and files without their original names may require manual matching; missing matches are reported in `local-image-manifest.json`. Rebuild variants whose filenames do not distinguish the variant also require manual verification.

Drawings remain the work of their original authors; collecting a reference image does not establish factory dimensional accuracy. Preserve credits and source links with each file.
