"""Archive the source and evidence without intermediate builds or native test copies."""
import os
import pathlib
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
destination = root.parent / 'UltraLightWallpaper-source.zip'
excluded = {'bin', 'obj', 'artifacts', '.git', '.vs', '__pycache__'}
with zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for directory, directories, files in os.walk(root):
        directories[:] = sorted(d for d in directories if d not in excluded)
        for name in sorted(files):
            path = pathlib.Path(directory, name)
            archive.write(path, pathlib.Path(root.name) / path.relative_to(root))
print(destination)
