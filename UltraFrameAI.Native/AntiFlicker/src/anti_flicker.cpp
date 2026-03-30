#include "anti_flicker.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <limits>
#include <vector>

namespace
{
    struct MotionBlock
    {
        int Dx = 0;
        int Dy = 0;
        float Weight = 0.0f;
    };

    enum class ContentMode
    {
        Video = 0,
        Anime = 1,
        Faces = 2,
        AnimeUltra = 3
    };

    struct ModeTuning
    {
        float FlickerGain;
        float MotionGain;
        float EdgeGuardScale;
        float MaxBlendScale;
        float FlatBoost;
    };

    static int ClampInt(int value, int minValue, int maxValue)
    {
        return std::max(minValue, std::min(maxValue, value));
    }

    static float ClampFloat(float value, float minValue, float maxValue)
    {
        return std::max(minValue, std::min(maxValue, value));
    }

    static std::uint8_t Luma(std::uint8_t b, std::uint8_t g, std::uint8_t r)
    {
        return static_cast<std::uint8_t>((29 * b + 150 * g + 77 * r) >> 8);
    }

    class AntiFlickerContext final
    {
    public:
        explicit AntiFlickerContext(const af_config& config)
            : m_width(std::max(1, config.width))
            , m_height(std::max(1, config.height))
            , m_channels(config.channels == 4 ? 4 : 3)
            , m_factor(std::max(2, config.downscale_factor))
            , m_blockSize(std::max(16, config.block_size))
            , m_searchRadius(std::max(1, config.search_radius))
            , m_contentMode(static_cast<ContentMode>(std::clamp(config.content_mode, 0, 3)))
            , m_blendStrength(ClampFloat(config.blend_strength, 0.0f, 1.0f))
            , m_maxBlend(ClampFloat(config.max_blend, 0.0f, 1.0f))
            , m_edgeGuard(ClampFloat(config.edge_guard, 0.0f, 2.0f))
            , m_strengthScale(ClampFloat(config.strength_scale, 0.0f, 1.0f))
            , m_downWidth((m_width + m_factor - 1) / m_factor)
            , m_downHeight((m_height + m_factor - 1) / m_factor)
            , m_blockCols((m_width + m_blockSize - 1) / m_blockSize)
            , m_blockRows((m_height + m_blockSize - 1) / m_blockSize)
            , m_prevFrame(static_cast<std::size_t>(m_width) * m_height * m_channels, 0)
            , m_currFrame(m_prevFrame.size(), 0)
            , m_prevDownLuma(static_cast<std::size_t>(m_downWidth) * m_downHeight, 0)
            , m_currDownLuma(m_prevDownLuma.size(), 0)
            , m_motion(static_cast<std::size_t>(m_blockCols) * m_blockRows)
        {
        }

        void Reset()
        {
            std::fill(m_prevFrame.begin(), m_prevFrame.end(), std::uint8_t{0});
            std::fill(m_prevDownLuma.begin(), m_prevDownLuma.end(), std::uint8_t{0});
            std::fill(m_motion.begin(), m_motion.end(), MotionBlock{});
            m_hasPrev = false;
        }

        int Process(const std::uint8_t* input, std::uint8_t* output, int byteCount)
        {
            if (input == nullptr || output == nullptr || byteCount < static_cast<int>(m_prevFrame.size()))
            {
                return -1;
            }

            std::memcpy(m_currFrame.data(), input, m_prevFrame.size());
            BuildDownscaledLuma(m_currFrame, m_currDownLuma);

            if (!m_hasPrev)
            {
                std::memcpy(output, m_currFrame.data(), m_prevFrame.size());
                std::memcpy(m_prevFrame.data(), output, m_prevFrame.size());
                std::memcpy(m_prevDownLuma.data(), m_currDownLuma.data(), m_currDownLuma.size());
                m_hasPrev = true;
                return 0;
            }

            const float sceneCutError = EstimateSceneChange();
            if (sceneCutError > 0.30f)
            {
                std::memcpy(output, m_currFrame.data(), m_prevFrame.size());
                std::memcpy(m_prevFrame.data(), output, m_prevFrame.size());
                std::memcpy(m_prevDownLuma.data(), m_currDownLuma.data(), m_currDownLuma.size());
                return 0;
            }

            BuildMotionField(sceneCutError);
            BlendFrame(output);

            std::memcpy(m_prevFrame.data(), output, m_prevFrame.size());
            std::memcpy(m_prevDownLuma.data(), m_currDownLuma.data(), m_currDownLuma.size());
            return 0;
        }

    private:
        void BuildDownscaledLuma(const std::vector<std::uint8_t>& frame, std::vector<std::uint8_t>& out)
        {
            for (int dy = 0; dy < m_downHeight; ++dy)
            {
                const int sy0 = dy * m_factor;
                for (int dx = 0; dx < m_downWidth; ++dx)
                {
                    const int sx0 = dx * m_factor;
                    int sum = 0;
                    int count = 0;
                    for (int yy = 0; yy < m_factor; ++yy)
                    {
                        const int sy = sy0 + yy;
                        if (sy >= m_height)
                        {
                            break;
                        }

                        const std::size_t rowBase = static_cast<std::size_t>(sy) * m_width * m_channels;
                        for (int xx = 0; xx < m_factor; ++xx)
                        {
                            const int sx = sx0 + xx;
                            if (sx >= m_width)
                            {
                                break;
                            }

                            const std::size_t index = rowBase + static_cast<std::size_t>(sx) * m_channels;
                            const std::uint8_t b = frame[index + 0];
                            const std::uint8_t g = frame[index + 1];
                            const std::uint8_t r = frame[index + 2];
                            sum += Luma(b, g, r);
                            ++count;
                        }
                    }

                    out[static_cast<std::size_t>(dy) * m_downWidth + dx] = static_cast<std::uint8_t>(count > 0 ? sum / count : 0);
                }
            }
        }

        float EstimateSceneChange() const
        {
            long long sad = 0;
            long long pixels = 0;
            for (std::size_t i = 0; i < m_currDownLuma.size(); ++i)
            {
                sad += std::abs(static_cast<int>(m_currDownLuma[i]) - static_cast<int>(m_prevDownLuma[i]));
                ++pixels;
            }

            return pixels > 0 ? static_cast<float>(sad) / static_cast<float>(pixels * 255.0) : 0.0f;
        }

        void BuildMotionField(float sceneCutError)
        {
            for (int by = 0; by < m_blockRows; ++by)
            {
                for (int bx = 0; bx < m_blockCols; ++bx)
                {
                    MotionBlock motion = EstimateBlockMotion(bx, by);
                    motion.Weight = EstimateBlockWeight(bx, by, motion, sceneCutError);
                    m_motion[static_cast<std::size_t>(by) * m_blockCols + bx] = motion;
                }
            }
        }

        MotionBlock EstimateBlockMotion(int blockX, int blockY) const
        {
            const int x0 = blockX * m_blockSize;
            const int y0 = blockY * m_blockSize;
            const int bw = std::min(m_blockSize, m_width - x0);
            const int bh = std::min(m_blockSize, m_height - y0);
            const int dsX0 = x0 / m_factor;
            const int dsY0 = y0 / m_factor;
            const int dsW = std::max(1, (bw + m_factor - 1) / m_factor);
            const int dsH = std::max(1, (bh + m_factor - 1) / m_factor);

            int bestDx = 0;
            int bestDy = 0;
            int bestSad = std::numeric_limits<int>::max();

            for (int dy = -m_searchRadius; dy <= m_searchRadius; ++dy)
            {
                for (int dx = -m_searchRadius; dx <= m_searchRadius; ++dx)
                {
                    const int candX0 = dsX0 + dx;
                    const int candY0 = dsY0 + dy;
                    if (candX0 < 0 || candY0 < 0 || candX0 + dsW > m_downWidth || candY0 + dsH > m_downHeight)
                    {
                        continue;
                    }

                    int sad = 0;
                    for (int yy = 0; yy < dsH; ++yy)
                    {
                        const std::size_t currRow = static_cast<std::size_t>(dsY0 + yy) * m_downWidth + dsX0;
                        const std::size_t prevRow = static_cast<std::size_t>(candY0 + yy) * m_downWidth + candX0;
                        for (int xx = 0; xx < dsW; ++xx)
                        {
                            sad += std::abs(static_cast<int>(m_currDownLuma[currRow + xx]) - static_cast<int>(m_prevDownLuma[prevRow + xx]));
                            if (sad >= bestSad)
                            {
                                break;
                            }
                        }

                        if (sad >= bestSad)
                        {
                            break;
                        }
                    }

                    if (sad < bestSad)
                    {
                        bestSad = sad;
                        bestDx = dx;
                        bestDy = dy;
                    }
                }
            }

            MotionBlock result;
            result.Dx = bestDx;
            result.Dy = bestDy;
            return result;
        }

        float EstimateBlockWeight(int blockX, int blockY, const MotionBlock& motion, float sceneCutError) const
        {
            const int x0 = blockX * m_blockSize;
            const int y0 = blockY * m_blockSize;
            const int bw = std::min(m_blockSize, m_width - x0);
            const int bh = std::min(m_blockSize, m_height - y0);
            const int dsX0 = x0 / m_factor;
            const int dsY0 = y0 / m_factor;
            const int dsW = std::max(1, (bw + m_factor - 1) / m_factor);
            const int dsH = std::max(1, (bh + m_factor - 1) / m_factor);

            int sad = 0;
            int edge = 0;
            const int candX0 = ClampInt(dsX0 + motion.Dx, 0, std::max(0, m_downWidth - dsW));
            const int candY0 = ClampInt(dsY0 + motion.Dy, 0, std::max(0, m_downHeight - dsH));

            for (int yy = 0; yy < dsH; ++yy)
            {
                const std::size_t currRow = static_cast<std::size_t>(dsY0 + yy) * m_downWidth + dsX0;
                const std::size_t prevRow = static_cast<std::size_t>(candY0 + yy) * m_downWidth + candX0;
                for (int xx = 0; xx < dsW; ++xx)
                {
                    const int curr = static_cast<int>(m_currDownLuma[currRow + xx]);
                    const int prev = static_cast<int>(m_prevDownLuma[prevRow + xx]);
                    sad += std::abs(curr - prev);
                    if (xx + 1 < dsW)
                    {
                        edge += std::abs(curr - static_cast<int>(m_currDownLuma[currRow + xx + 1]));
                    }
                    if (yy + 1 < dsH)
                    {
                        edge += std::abs(curr - static_cast<int>(m_currDownLuma[currRow + xx + m_downWidth]));
                    }
                }
            }

            const float area = static_cast<float>(std::max(1, dsW * dsH));
            const float reprojection = ClampFloat(static_cast<float>(sad) / (area * 255.0f), 0.0f, 1.0f);
            const float motionMag = ClampFloat((std::abs(motion.Dx) + std::abs(motion.Dy)) / static_cast<float>(m_searchRadius * 2), 0.0f, 1.0f);
            const float edgeMag = ClampFloat(static_cast<float>(edge) / (area * 255.0f * 2.0f), 0.0f, 1.0f);
            const float flatMag = 1.0f - edgeMag;

            const auto tuning = GetModeTuning();
            const float adjustedMotionGate = 1.0f - ClampFloat(motionMag * tuning.MotionGain, 0.0f, 1.0f);
            const float detailPenalty = ClampFloat(edgeMag * m_edgeGuard * tuning.EdgeGuardScale, 0.0f, 1.0f);
            const float sceneGate = 1.0f - ClampFloat(sceneCutError * 2.0f, 0.0f, 1.0f);
            const float tunedFlickerScore = ClampFloat(reprojection * adjustedMotionGate * tuning.FlickerGain * (1.0f + flatMag * tuning.FlatBoost), 0.0f, 1.0f);
            const float weight = m_blendStrength * tunedFlickerScore * (1.0f - detailPenalty) * sceneGate * m_strengthScale;

            return ClampFloat(weight, 0.0f, m_maxBlend * m_strengthScale * tuning.MaxBlendScale);
        }

        void BlendFrame(std::uint8_t* output)
        {
            if (m_channels != 3 && m_channels != 4)
            {
                std::memcpy(output, m_currFrame.data(), m_currFrame.size());
                return;
            }

            for (int y = 0; y < m_height; ++y)
            {
                const int by = std::min(y / m_blockSize, m_blockRows - 1);
                const int by1 = std::min(by + 1, m_blockRows - 1);
                const float ty = m_blockSize > 1 ? static_cast<float>(y - by * m_blockSize) / static_cast<float>(m_blockSize) : 0.0f;

                for (int x = 0; x < m_width; ++x)
                {
                    const int bx = std::min(x / m_blockSize, m_blockCols - 1);
                    const int bx1 = std::min(bx + 1, m_blockCols - 1);
                    const float tx = m_blockSize > 1 ? static_cast<float>(x - bx * m_blockSize) / static_cast<float>(m_blockSize) : 0.0f;

                    const MotionBlock& m00 = m_motion[static_cast<std::size_t>(by) * m_blockCols + bx];
                    const MotionBlock& m10 = m_motion[static_cast<std::size_t>(by) * m_blockCols + bx1];
                    const MotionBlock& m01 = m_motion[static_cast<std::size_t>(by1) * m_blockCols + bx];
                    const MotionBlock& m11 = m_motion[static_cast<std::size_t>(by1) * m_blockCols + bx1];

                    const float topX = Lerp(static_cast<float>(m00.Dx), static_cast<float>(m10.Dx), tx);
                    const float botX = Lerp(static_cast<float>(m01.Dx), static_cast<float>(m11.Dx), tx);
                    const float topY = Lerp(static_cast<float>(m00.Dy), static_cast<float>(m10.Dy), tx);
                    const float botY = Lerp(static_cast<float>(m01.Dy), static_cast<float>(m11.Dy), tx);
                    const float motionX = Lerp(topX, botX, ty);
                    const float motionY = Lerp(topY, botY, ty);

                    const float topW = Lerp(m00.Weight, m10.Weight, tx);
                    const float botW = Lerp(m01.Weight, m11.Weight, tx);
                    const float weight = ClampFloat(Lerp(topW, botW, ty), 0.0f, m_maxBlend);

                    const int sampleX = ClampInt(static_cast<int>(std::lround(x + motionX * m_factor)), 0, m_width - 1);
                    const int sampleY = ClampInt(static_cast<int>(std::lround(y + motionY * m_factor)), 0, m_height - 1);

                    const std::size_t currentIndex = (static_cast<std::size_t>(y) * m_width + x) * m_channels;
                    const std::size_t sampleIndex = (static_cast<std::size_t>(sampleY) * m_width + sampleX) * m_channels;

                    for (int c = 0; c < 3; ++c)
                    {
                        const float curr = static_cast<float>(m_currFrame[currentIndex + c]);
                        const float prev = static_cast<float>(m_prevFrame[sampleIndex + c]);
                        output[currentIndex + c] = static_cast<std::uint8_t>(std::lround(curr * (1.0f - weight) + prev * weight));
                    }

                    if (m_channels == 4)
                    {
                        output[currentIndex + 3] = m_currFrame[currentIndex + 3];
                    }
                }
            }
        }

        static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        ModeTuning GetModeTuning() const
        {
            switch (m_contentMode)
            {
            case ContentMode::Video:
                return { 0.88f, 1.00f, 0.92f, 0.88f, 0.10f };
            case ContentMode::Faces:
                return { 0.62f, 0.80f, 1.38f, 0.62f, 0.04f };
            case ContentMode::AnimeUltra:
                return { 1.48f, 0.70f, 1.50f, 1.18f, 0.34f };
            case ContentMode::Anime:
            default:
                return { 1.30f, 0.78f, 1.34f, 1.08f, 0.22f };
            }
        }

        const int m_width;
        const int m_height;
        const int m_channels;
        const int m_factor;
        const int m_blockSize;
        const int m_searchRadius;
        const ContentMode m_contentMode;
        const float m_blendStrength;
        const float m_maxBlend;
        const float m_edgeGuard;
        const float m_strengthScale;
        const int m_downWidth;
        const int m_downHeight;
        const int m_blockCols;
        const int m_blockRows;

        bool m_hasPrev = false;
        std::vector<std::uint8_t> m_prevFrame;
        std::vector<std::uint8_t> m_currFrame;
        std::vector<std::uint8_t> m_prevDownLuma;
        std::vector<std::uint8_t> m_currDownLuma;
        std::vector<MotionBlock> m_motion;
    };
}

AF_API void* af_create(const af_config* config)
{
    if (config == nullptr || config->width <= 0 || config->height <= 0 || (config->channels != 3 && config->channels != 4))
    {
        return nullptr;
    }

    try
    {
        return new AntiFlickerContext(*config);
    }
    catch (...)
    {
        return nullptr;
    }
}

AF_API void af_destroy(void* handle)
{
    delete static_cast<AntiFlickerContext*>(handle);
}

AF_API void af_reset(void* handle)
{
    if (handle == nullptr)
    {
        return;
    }

    static_cast<AntiFlickerContext*>(handle)->Reset();
}

AF_API int af_process(void* handle, const std::uint8_t* input_bgr, std::uint8_t* output_bgr, int byte_count)
{
    if (handle == nullptr)
    {
        return -1;
    }

    return static_cast<AntiFlickerContext*>(handle)->Process(input_bgr, output_bgr, byte_count);
}
