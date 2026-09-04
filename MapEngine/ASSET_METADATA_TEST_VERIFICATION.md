# Asset Metadata System - Test Verification

## Summary

Phase 4-6 implementation completed on 2026-08-04. This document outlines the manual test cases to verify the asset import, deduplication, and metadata synchronization features.

## Implemented Features

### Phase 4.1: AssetImporter Integration ✓
- `AssetLibraryFileSystemService` now uses `AssetImporter.ImportImage()`
- Content-based hashing and metadata registration on import
- Automatic metadata index updates

### Phase 4.2: Deduplication Prompt ✓
- Dialog appears when duplicate content detected
- User can choose to create duplicate reference or skip
- Duplicate .asset files reference the same image hash

### Phase 4.3: Map Drag Reference ✓
- Assets use hash-based references (`assetRef` field)
- Map instances read hash from .asset files
- No additional conversion needed (already content-addressed)

### Phase 5.1: File Rename Support ✓
- `AssetMetadataStore.UpdateRelativePath()` implemented
- `AssetLibraryFileSystemService.RenameFile()` updates metadata
- `RenameFolder()` batch updates all child files

### Phase 5.2: File Move/Copy Support ✓
- `AssetMetadataStore.UpdateRelativePathPrefix()` for folder moves
- `AssetMetadataStore.ClonePathPrefix()` for folder copies
- `CopyFileToFolder()` and `CopyFolderToParent()` update metadata

## Test Cases

### Test 1: Import New Image
**Steps**:
1. Launch MapEngine.Shell or MapEngine.Avalonia
2. Open Asset Library panel
3. Drag a PNG/JPG image file into a folder
4. Verify .asset file created with user-friendly name
5. Check `AssetLibrary/.metadata/asset-index.json` contains entry with:
   - Hash key (normalized, lowercase)
   - RelativePath pointing to the .asset file
   - FileName, MimeType, Size, ImportedAt fields

**Expected Result**: ✓ Image imported, metadata registered, .asset file created

---

### Test 2: Import Duplicate Content
**Steps**:
1. Import an image (e.g., `forest.png`)
2. Make a copy of the same image with different name (e.g., `forest_copy.png`)
3. Drag `forest_copy.png` into Asset Library
4. Dialog should appear: "检测到重复内容"
5. Click "创建副本" button

**Expected Result**: 
- ✓ Dialog appears asking user choice
- ✓ Second .asset file created referencing same image hash
- ✓ Only one physical image file stored
- ✓ Both .asset files visible in Asset Library

---

### Test 3: Skip Duplicate Import
**Steps**:
1. Import an image
2. Try importing the same content again
3. Dialog appears
4. Click "跳过" button

**Expected Result**:
- ✓ Dialog appears
- ✓ No new .asset file created
- ✓ Asset Library unchanged

---

### Test 4: Rename File
**Steps**:
1. Right-click an .asset file in Asset Library
2. Select "重命名"
3. Enter new name: "RenamedAsset"
4. Check `asset-index.json` for updated RelativePath

**Expected Result**:
- ✓ File renamed on disk
- ✓ Metadata RelativePath updated
- ✓ Asset Library reflects new name

---

### Test 5: Rename Folder
**Steps**:
1. Create folder with multiple assets inside
2. Right-click folder, select "重命名"
3. Rename to "NewFolderName"
4. Check `asset-index.json` for all child files

**Expected Result**:
- ✓ Folder renamed on disk
- ✓ All child asset RelativePaths updated with new folder prefix
- ✓ Asset Library shows updated hierarchy

---

### Test 6: Copy File
**Steps**:
1. Right-click an .asset file
2. Select "复制"
3. Navigate to target folder
4. Paste
5. Check `asset-index.json` for new entry

**Expected Result**:
- ✓ New .asset file created with auto-numbered name
- ✓ New metadata entry created with same hash
- ✓ Both files reference the same image content

---

### Test 7: Copy Folder
**Steps**:
1. Right-click a folder with assets
2. Select "复制到"
3. Choose target parent folder
4. Check `asset-index.json` for cloned entries

**Expected Result**:
- ✓ Folder duplicated with all contents
- ✓ All child assets have new metadata entries
- ✓ All entries reference correct hashes

---

### Test 8: Drag Asset to Map
**Steps**:
1. Open a scene
2. Drag an .asset file from Asset Library onto the map canvas
3. Token should appear with sprite rendered
4. Check HierarchyItem `AssetRef` property contains hash

**Expected Result**:
- ✓ Token created on map at drop position
- ✓ Sprite loads and displays correctly
- ✓ AssetRef contains `sha256:` prefixed hash

---

### Test 9: Save and Reload Scene
**Steps**:
1. Import assets and create tokens on map
2. Save scene (File → Save)
3. Close application
4. Reopen application and load scene
5. Verify all tokens visible with correct sprites

**Expected Result**:
- ✓ Scene saves successfully
- ✓ Scene loads successfully
- ✓ All tokens and sprites restored
- ✓ Metadata index preserved

---

### Test 10: Delete Asset File
**Steps**:
1. Delete an .asset file from Asset Library
2. Physical file should be removed
3. Note: Image file (hash-named) remains for other references

**Expected Result**:
- ✓ .asset file deleted
- ✓ Asset Library no longer shows the item
- ✓ Image file persists if other .asset files reference it
- ✓ Metadata entry may remain (cleanup not implemented yet)

---

## Known Limitations

1. **No automatic cleanup of orphaned image files**: When all .asset files referencing an image are deleted, the physical image file remains. Manual cleanup required.

2. **Metadata grows over time**: Deleted assets leave entries in `asset-index.json`. A compaction tool is not yet implemented.

3. **No conflict resolution UI**: If metadata file is corrupted, the system creates a fresh empty index silently.

## Code Changes Summary

### Modified Files
- `MapEngine.Core/World/AssetMetadataStore.cs` - Added path update methods
- `MapEngine.Avalonia/Services/AssetLibraryFileSystemService.cs` - Integrated metadata updates
- `MapEngine.Avalonia/Views/Partials/MapEditorView.AssetLibrary.cs` - Deduplication dialog

### New Methods
- `AssetMetadataStore.UpdateRelativePath(oldPath, newPath)`
- `AssetMetadataStore.UpdateRelativePathPrefix(oldPrefix, newPrefix)`
- `AssetMetadataStore.ClonePathPrefix(sourcePrefix, targetPrefix)`

### Build Status
✓ Build successful (0 errors, 7 warnings about ImageSharp vulnerability)

### Test Status
- Unit tests: 78 passed, 5 failed (pre-existing TokenUIOverlay failures)
- Manual tests: Pending verification

## Next Steps

1. Perform manual testing using test cases above
2. Fix any issues discovered during testing
3. Consider implementing metadata compaction tool
4. Consider implementing orphaned file cleanup tool
5. Update user documentation with import workflow

## Related Documents
- [ASSET_IMPORT_AND_LIBRARY_SOURCE.md](ASSET_IMPORT_AND_LIBRARY_SOURCE.md) - Import and library architecture
- [ASSET_HASH_REFERENCE_DESIGN.md](ASSET_HASH_REFERENCE_DESIGN.md) - Hash-based reference design
