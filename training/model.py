"""Small policy network with a stable ONNX tensor contract."""

import torch
from torch import nn


class PolicyNet(nn.Module):
    def __init__(self, channels: int = 32):
        super().__init__()
        self.features = nn.Sequential(
            nn.Conv2d(3, channels, 3, padding=1), nn.ReLU(),
            nn.Conv2d(channels, channels, 3, padding=1), nn.ReLU(),
            nn.Conv2d(channels, channels, 3, padding=1), nn.ReLU(),
            nn.Conv2d(channels, 1, 1),
        )

    def forward(self, board: torch.Tensor) -> torch.Tensor:
        return self.features(board).flatten(start_dim=1)
