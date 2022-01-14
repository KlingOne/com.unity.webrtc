using UnityEngine;

public class BarcodeDecoder : MonoBehaviour
{
    [SerializeField] int Row;
    [SerializeField] int Column;
    [SerializeField] ComputeShader Shader;

    GraphicsBuffer readbackBuffer_;
    int kernelIndex_;
    Color[] data_;

    private void Awake()
    {
        int count = Row * Column;
        int stride = sizeof(float) * 4;
        readbackBuffer_ =
            new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
        data_ = new Color[count];
        kernelIndex_ = Shader.FindKernel("Read");
    }

    private void OnDestroy()
    {
        readbackBuffer_.Dispose();
    }

    public int GetValue(Texture texture)
    {
        return Decode(texture);
    }

    int Decode(Texture source)
    {
        Shader.SetTexture(kernelIndex_, "Source", source);
        Shader.SetInt("Row", Row);
        Shader.SetInt("Column", Column);
        Shader.SetBuffer(kernelIndex_, "Result", readbackBuffer_);
        Shader.Dispatch(kernelIndex_, Row, Column, 1);

        readbackBuffer_.GetData(data_);

        int value = 0;
        for (int i = 0; i < data_.Length; i++)
        {
            if (data_[i].grayscale > 0.5)
                value += 1 << i;
        }
        return value;
    }
}
