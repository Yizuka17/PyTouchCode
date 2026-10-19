"""Install the learning packages from Settings > Environment updates first."""
import numpy as np
from sklearn.linear_model import LinearRegression

features = np.array([[1.0], [2.0], [3.0], [4.0]])
labels = np.array([3.0, 5.0, 7.0, 9.0])
model = LinearRegression().fit(features, labels)
print("预测：", model.predict([[5.0]]))
print("数据：", features.dtype, features.shape)
