# feet.py <src> [x0 x1] : 後ろ足のあたり(既定は絵の左3割)のいちばん下の y と、全体の下端
import sys; sys.path.insert(0, r'C:/GameProject/2ndAction/Tools/art')
from reaperpose import key
import numpy as np
from PIL import Image
a = np.asarray(key(Image.open(sys.argv[1])))[..., 3] > 0
xs = np.nonzero(a.any(0))[0]; x0, x1 = (int(sys.argv[2]), int(sys.argv[3])) if len(sys.argv) > 3 else (xs.min(), xs.min() + int((xs.max() - xs.min()) * 0.3))
print('feet', int(np.nonzero(a[:, x0:x1].any(1))[0].max()) + 1, 'all', int(np.nonzero(a.any(1))[0].max()) + 1, 'x', x0, x1)
